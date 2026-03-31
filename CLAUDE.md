# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```sh
# Build
dotnet build JukeVox.slnx

# Run tests (NUnit)
dotnet test JukeVox.slnx

# Run backend (https://127.0.0.1:5001)
cd src/JukeVox.Server && dotnet run

# Run frontend dev server (http://localhost:5173)
cd src/JukeVox.Client && npm run dev

# Lint frontend
cd src/JukeVox.Client && npm run lint
```

Spotify credentials are provided via environment variables (`SPOTIFY__ClientId`, `SPOTIFY__ClientSecret`, `SPOTIFY__RedirectUri`). See `.env.example`.

## Architecture

JukeVox is a collaborative Spotify queue app. A host creates a party, connects their Spotify account, and guests join via link/QR to add songs.

**Backend:** .NET 10 ASP.NET Core Web API + SignalR (`src/JukeVox.Server/`)
**Frontend:** React 19 + TypeScript + Vite (`src/JukeVox.Client/`)
**Tests:** NUnit 4 + FluentAssertions + Moq (`tests/JukeVox.Server.Tests/`)

### Backend request flow

1. `PartySessionMiddleware` extracts/creates an encrypted session ID from the `JukeVox.SessionId` cookie, stores it in `HttpContext.Items["SessionId"]`
2. `PartyContextMiddleware` resolves the session's party ID (if any) and stores it in `IPartyContextAccessor` for downstream use
3. Controllers access session ID via `HttpContext.GetSessionId()` extension method
4. Controllers delegate to singleton services (`PartyService`, `QueueService`) for state mutations
5. After mutations, controllers broadcast updates to clients via `IHubContext<PartyHub, IPartyClient>`

### Key services

- **PartyService** — Singleton. Manages all active `Party` objects (one per host, keyed by party ID). Uses per-party `Lock` instances for concurrency. Persists each party to its own JSON file in `parties/` after every mutation.
- **QueueService** — Singleton. Manages queue add/remove/reorder/dequeue. Uses per-party `Lock` instances (separate from `PartyService` locks). Injects `IHubContext` to broadcast `QueueUpdated` after changes.
- **PlaybackMonitorService** — `BackgroundService` that polls Spotify every 2 seconds. See [Playback Monitor](#playback-monitor) section below.
- **SpotifyAuthService** — Handles OAuth authorization code flow and token refresh. Tokens are stored in the `Party` model and auto-refreshed with a 1-minute expiry buffer.
- **SpotifyPlayerService** — HTTP wrapper for Spotify Web API player endpoints. Retries on 429 with `Retry-After` header (recursive, no max depth).
- **HostCredentialService** — Manages WebAuthn credentials for multiple hosts. First host registers via setup token; additional hosts register via admin-generated invite codes (one-time use). Stores credentials in `host-credentials/` directory. Tracks admin status (first registered host is admin).
- **ConnectionMapping** — Bidirectional `ConcurrentDictionary` mapping session IDs ↔ SignalR connection IDs. Used for targeted broadcasts and cleanup on disconnect.

### Thread safety

`PartyService` and `QueueService` are singletons using per-party C# 13 `Lock` instances (not `object` locks, not a single global lock). Each party has its own lock in both services, retrieved via `GetPartyLock(partyId)`. Every public method that touches party state must acquire the appropriate party lock.

### SignalR events

`PartyHub` groups clients by party ID. On connection, `JoinPartyGroup` reads the encrypted session cookie from `HttpContext.Items["SessionId"]` (set by `PartySessionMiddleware`) and validates the caller is a participant before adding them to the SignalR group. Server-to-client messages defined in `IPartyClient`:

| Event | Payload | Trigger |
|---|---|---|
| `NowPlayingChanged` | `NowPlayingDto` | Track change, seek, play/pause |
| `PlaybackStateUpdated` | `PlaybackStateDto` | Playback monitor poll |
| `QueueUpdated` | `List<QueueItemDto>` | Add, remove, reorder, vote, dequeue |
| `CreditsUpdated` | `int` | Credit grant or spend |
| `PartyEnded` | *(none)* | Host ends party |

### Frontend state management

`PartyContext` (React Context) is the single source of truth. On mount it checks for an existing session via `GET /api/party/state`. When a party is active, it creates a SignalR connection that updates `nowPlaying`, `queue`, and `credits` in real time. REST calls go through `src/api/client.ts` with `credentials: 'include'` for cookie auth.

When the tab regains visibility, `PartyContext` fetches fresh state via REST to ensure the UI is current. SignalR callbacks are not muted — duplicate data from SignalR is harmless since `useAnimatedList` diffs by key and identical data produces no animations.

### Vite proxy

The frontend dev server proxies `/api/*` and `/hubs/*` (including WebSocket upgrades) to `https://127.0.0.1:5001`.

## Queue Sorting & Voting

The queue uses a 3-tier sort system. Key file: `Services/QueueService.cs` — `SortQueue()`, `Vote()`, `Reorder()`.

### Sort tiers (top to bottom)

| Tier | Criteria | Internal sort |
|---|---|---|
| 0 — Promoted | Score >= 3 | Score desc, then InsertionOrder asc |
| 1 — Regular | Not base playlist, not promoted | InsertionOrder asc (FIFO) |
| 2 — Base playlist | `IsFromBasePlaylist == true` | InsertionOrder asc (FIFO) |

Votes always win — there is no host pinning. The host can reorder within tiers via drag-and-drop, but vote-based promotion/demotion overrides manual ordering.

### Voting thresholds

- **Promotion:** Score >= 3 → item moves to Tier 0 (including base playlist items)
- **Auto-remove:** Score <= -3 → item removed from queue entirely
- `SortQueue()` is only called on threshold crossings (promotion gained/lost), not on every vote

### Base playlist promotion

When a base playlist item reaches Score >= 3, it promotes to Tier 0 like any other item. The frontend treats it as a non-base item for display purposes (`isFromBasePlaylist && score < 3` determines base playlist styling/divider). If the score drops below 3, it visually returns to the base playlist section. The `IsFromBasePlaylist` flag on the model is never mutated by votes.

### InsertionOrder

`InsertionOrder` is set from `Party.NextInsertionOrder` (an incrementing counter). It serves as the FIFO tiebreaker within tiers. Items can have InsertionOrders that don't match their list index (from legacy state or host reorder).

### Host reorder

When the host reorders via drag-and-drop, InsertionOrders are reassigned sequentially to reflect the new order. This controls ordering within tiers but cannot override vote-based promotion. The frontend clamps drag targets so items cannot be dragged across the base playlist boundary — base playlist items can only be reordered among themselves, and non-base items stay above the boundary.

### Score

`QueueItem.Score` is a computed getter-only property: `Votes.Values.Sum()`. The authoritative data is the `Votes` dictionary (keyed by session ID). Score serializes to `party-state.json` but has no setter, so it's ignored on deserialization.

## Playback Monitor

Key file: `Services/PlaybackMonitorService.cs`

- **Poll interval:** 2 seconds
- **Grace period:** 5 seconds after a controller-initiated track change (`NotifyTrackStarted`). During this window, foreign-track detection is skipped because Spotify may briefly report the old track.
- **Track end detection:** Triggered when playback stops on the same track and previous progress was within 5 seconds of duration (or progress reset backwards).
- **Foreign track detection:** After grace period, if Spotify moves to a track we didn't start, the monitor takes over: dequeues next item if queue has items, otherwise clears tracking state.
- **Idle mode:** Entered when queue is empty after dequeue. `_idleWatching = true`. If items are added and Spotify is idle, the monitor auto-starts playback.
- **Device fallback:** `PlayWithDeviceFallback` tries the last known device first, then falls back to: previously active device → any active device → first device in list.

## Authentication

### Guest sessions

`PartySessionMiddleware` auto-creates an encrypted `JukeVox.SessionId` cookie on first request (24-hour TTL, `HttpOnly`, `Secure`, `SameSite=Lax`). No login required — the session ID is the guest's identity. The cookie value is encrypted via ASP.NET Data Protection with a timestamp; the middleware validates TTL on every request and re-issues if expired.

Guest display names must be unique per party (case-insensitive). Attempting to join with a taken name returns 409 Conflict.

### Host auth

`JukeVox.HostAuth` cookie, encrypted via ASP.NET Data Protection. 24-hour TTL. Uses `AddEphemeralDataProtection()` — cookies become invalid after server restart. On logout, the session-to-party mapping is also cleared so the session doesn't retain stale host state.

### Passkey (WebAuthn)

Fido2NetLib v4 handles registration and assertion. Supports multiple hosts:

**First host (admin) setup:**
1. First server run generates a one-time setup token from `/usr/share/dict/words` (printed to console). Format: `word<0-99><symbol>` x4, e.g. `humble67#forest29!ocean41=bright93?`. Requires the `words` package.
2. Host visits `/host/setup`, enters the token, registers a passkey
3. Credential stored in `host-credentials/` directory
4. This host becomes the admin

**Additional host registration:**
1. Admin generates an invite code from `/host/admin` (one-time use, only one valid at a time)
2. New host visits `/host/register`, enters invite code and display name, registers a passkey
3. Each host is limited to one active party at a time

**Login:** All hosts authenticate at `/host` via passkey assertion.

### Spotify OAuth

Authorization code flow with CSRF state cookie (10-minute TTL, narrow `Path=/api/auth`). Redirect URI: `https://127.0.0.1:5001/api/auth/callback`.

## Gotchas

- **Overscroll prevention** (`src/overscroll-prevention.ts`): Prevents pinch-zoom and overscroll on mobile. Scrollable containers are auto-detected via computed `overflow` styles — no attributes needed. Range inputs (`<input type="range">`) are excluded so sliders work on mobile.
- **Visibility change refresh**: When tab regains focus, REST fetch refreshes state. SignalR is not muted — `useAnimatedList` handles duplicate/identical data gracefully.
- **NowPlaying uses requestAnimationFrame**: Progress bar updates at 60fps via direct DOM writes (refs, not React state). Seeking uses `pendingSeekRef` to ignore server updates within 3 seconds of a seek. No React re-renders during normal playback. Marquee scrolling for long track/artist names uses the Web Animations API (not CSS animations) with computed keyframe offsets so hold time is fixed regardless of text length.
- **index.html range input exception**: The overscroll-prevention script has a special early return for `<input type="range">` elements so the seek slider works on mobile.
- **Ambient art crossfade**: `useAmbientCrossfade` hook uses two alternating DOM layers (not CSS `::before`) because CSS can't transition between `url()` values. Layers use `translateZ(0)` + `will-change: opacity` to force GPU compositing (prevents deferred paint with heavy blur filters). Ref updates are deferred to async callbacks (preload `.then()`) to survive StrictMode's unmount+remount cycle.
- **Ephemeral Data Protection**: Host auth cookies are invalid after server restart (uses `AddEphemeralDataProtection`).
- **InsertionOrder vs physical position**: Items can have InsertionOrders that don't match their list index (from host reorder). `SortQueue` uses InsertionOrder for FIFO within tiers, not list position.
- **Spotify rate limiting**: `SpotifyPlayerService.SendAsync` retries on 429 with `Retry-After` header (defaults to 1s). Recursive retry with no max depth.
- **Join tokens**: Parties use UUID-based join tokens (`Guid.NewGuid().ToString("N")`, 32 hex chars). Guests join via `/join/:token` URL shared by the host through the share overlay (link/QR). There is no manual code entry — the token is only transmitted via URL.
- **React StrictMode**: Double-renders in dev (not production). Can cause duplicate API calls during development.
- **Vite HMR in dev**: Component re-renders on code changes can cause SignalR reconnections and state re-fetches. Does not happen in production.
- **ConnectionMapping**: Maps session IDs ↔ SignalR connection IDs. Used by `HostPartyController` to target individual guests for `CreditsUpdated` and `PartyEnded` broadcasts.

## API Route Map

| Prefix | Auth | Purpose |
|---|---|---|
| `/api/party/*` | Session cookie | Guest endpoints (join, leave, state) |
| `/api/host/*` | HostAuth cookie | Host party management (create, end, settings) |
| `/api/host/hosts` | HostAuth (admin) | Host credential management |
| `/api/host/invite-codes` | HostAuth (admin) | Generate host registration invite codes |
| `/api/host/register/*` | Invite code | Additional host registration |
| `/api/queue/*` | Session (add/vote), HostAuth (remove/reorder) | Queue operations |
| `/api/playback/*` | HostAuth cookie | Playback control (play, pause, skip, seek, volume) |
| `/api/search` | Participant (host or joined guest) | Spotify search |
| `/api/auth/*` | *(none)* | Spotify OAuth flow |
| `/hubs/party` | Session cookie (validated participant) | SignalR hub |

## Frontend Component Map

| File | Responsibility |
|---|---|
| `PartyContext.tsx` | Single source of truth, SignalR lifecycle, visibility refresh |
| `PartyLayout.tsx` | Shared layout shell for PartyPage and HostPortalPage (scroll sentinel, sticky header, slide-track panels, mobile tab nav) |
| `NowPlaying.tsx` | RAF-based progress bar, seeking, quip generation, ambient art crossfade, marquee |
| `QueueList.tsx` | Voting (optimistic updates), drag-and-drop reorder (host only, clamped at base playlist boundary) |
| `SearchOverlay.tsx` / `useSearch.ts` | Debounced search with request cancellation |
| `ShareOverlay.tsx` | QR code + copy/share join link |
| `HostControls.tsx` | Playback buttons, volume slider (debounced 300ms) |
| `ManagePanel.tsx` | Guest list, credits, kick, end party |
| `DeviceSelector.tsx` | Spotify device picker |
| `BasePlaylistSelector.tsx` | Base playlist picker |
| `HelpOverlay.tsx` | Voting rules explanation for guests, leave party button |
| `JoinPage.tsx` | `/join/:token` route — handles join flow, existing-session detection, party switching |
| `HostAdminPage.tsx` | Admin page — invite code generation, host credential management |
| `HostRegisterPage.tsx` | Additional host registration via invite code + passkey |
