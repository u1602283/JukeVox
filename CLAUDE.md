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

JukeVox is a collaborative Spotify queue app. A host creates a party, connects their Spotify account, and guests join via invite code to add songs.

**Backend:** .NET 10 ASP.NET Core Web API + SignalR (`src/JukeVox.Server/`)
**Frontend:** React 19 + TypeScript + Vite (`src/JukeVox.Client/`)
**Tests:** NUnit 4 + FluentAssertions + Moq (`tests/JukeVox.Server.Tests/`)

### Backend request flow

1. `PartySessionMiddleware` extracts/creates a session ID from the `JukeVox.SessionId` cookie, stores it in `HttpContext.Items["SessionId"]`
2. Controllers access it via `HttpContext.GetSessionId()` extension method
3. Controllers delegate to singleton services (`PartyService`, `QueueService`) for state mutations
4. After mutations, controllers broadcast updates to clients via `IHubContext<PartyHub, IPartyClient>`

### Key services

- **PartyService** — Singleton. Owns the single active `Party` object. All public methods acquire a `Lock` before reading/writing state. Persists state to `party-state.json` after every mutation.
- **QueueService** — Singleton. Manages queue add/remove/reorder/dequeue. Also lock-synchronized. Injects `IHubContext` to broadcast `QueueUpdated` after changes.
- **PlaybackMonitorService** — `BackgroundService` that polls Spotify every 2 seconds. See [Playback Monitor](#playback-monitor) section below.
- **SpotifyAuthService** — Handles OAuth authorization code flow and token refresh. Tokens are stored in the `Party` model and auto-refreshed with a 1-minute expiry buffer.
- **SpotifyPlayerService** — HTTP wrapper for Spotify Web API player endpoints. Retries on 429 with `Retry-After` header (recursive, no max depth).
- **ConnectionMapping** — Bidirectional `ConcurrentDictionary` mapping session IDs ↔ SignalR connection IDs. Used for targeted broadcasts and cleanup on disconnect.

### Thread safety

`PartyService` and `QueueService` are singletons using C# 13 `Lock` (not `object` locks). Every public method that touches state must acquire the lock.

### SignalR events

`PartyHub` groups clients by party ID. Server-to-client messages defined in `IPartyClient`:

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

The queue uses a 4-tier sort system. Key file: `Services/QueueService.cs` — `SortQueue()`, `Vote()`, `Reorder()`.

### Sort tiers (top to bottom)

| Tier | Criteria | Internal sort |
|---|---|---|
| 0 — Promoted | Score >= 3 and not HostPinned | Score desc, then InsertionOrder asc |
| 1 — Host-pinned | `HostPinned == true` | InsertionOrder asc (host's explicit order) |
| 2 — Regular | Not base playlist, not pinned, not promoted | InsertionOrder asc (FIFO) |
| 3 — Base playlist | `IsFromBasePlaylist == true` | InsertionOrder asc (FIFO) |

### Voting thresholds

- **Promotion:** Score >= 3 → item moves to Tier 0
- **Auto-remove:** Score <= -3 → item removed from queue entirely
- `SortQueue()` is only called on threshold crossings (promotion gained/lost), not on every vote

### InsertionOrder

`InsertionOrder` is set from `Party.NextInsertionOrder` (an incrementing counter). It serves as the FIFO tiebreaker within tiers. Items can have InsertionOrders that don't match their list index (from legacy state or host reorder).

### Host reorder

When the host reorders via drag-and-drop, `HostPinned` is set on **all** items and InsertionOrders are reassigned sequentially. This locks the entire queue into Tier 1 (host's explicit order) until a vote crosses a threshold.

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

`PartySessionMiddleware` auto-creates a `JukeVox.SessionId` cookie on first request. No login required — the session ID is the guest's identity.

### Host auth

`JukeVox.HostAuth` cookie, encrypted via ASP.NET Data Protection. 24-hour TTL. Uses `AddEphemeralDataProtection()` — cookies become invalid after server restart.

### Passkey (WebAuthn)

Fido2NetLib v4 handles registration and assertion. Setup flow:

1. First server run generates a one-time setup token from `/usr/share/dict/words` (printed to console). Format: `word<0-99><symbol>` x4, e.g. `humble67#forest29!ocean41=bright93?`. Requires the `words` package.
2. Host visits `/host/setup`, enters the token, registers a passkey
3. Credential stored in `host-credential.json`
4. Subsequent logins at `/host` use passkey assertion

### Spotify OAuth

Authorization code flow with CSRF state cookie (10-minute TTL, narrow `Path=/api/auth`). Redirect URI: `https://127.0.0.1:5001/api/auth/callback`.

## Gotchas

- **index.html inline script**: Prevents pinch-zoom (`gesturestart`) and blocks overscroll on elements that aren't marked `[data-scrollable]`. If a scrollable component doesn't scroll on mobile, add `data-scrollable` to it.
- **Visibility change refresh**: When tab regains focus, REST fetch refreshes state. SignalR is not muted — `useAnimatedList` handles duplicate/identical data gracefully.
- **NowPlaying uses requestAnimationFrame**: Progress bar updates at 60fps via direct DOM writes (refs, not React state). Seeking uses `pendingSeekRef` to ignore server updates within 3 seconds of a seek. No React re-renders during normal playback.
- **Ephemeral Data Protection**: Host auth cookies are invalid after server restart (uses `AddEphemeralDataProtection`).
- **InsertionOrder vs physical position**: Items can have InsertionOrders that don't match their list index (from legacy state or host reorder). `SortQueue` uses InsertionOrder for FIFO, not list position.
- **Spotify rate limiting**: `SpotifyPlayerService.SendAsync` retries on 429 with `Retry-After` header (defaults to 1s). Recursive retry with no max depth.
- **Invite code alphabet**: `ABCDEFGHJKMNPQRSTUVWXYZ23456789` — excludes I, O, L, 0, 1 to prevent confusion. 6-character codes generated with `RandomNumberGenerator`.
- **React StrictMode**: Double-renders in dev (not production). Can cause duplicate API calls during development.
- **Vite HMR in dev**: Component re-renders on code changes can cause SignalR reconnections and state re-fetches. Does not happen in production.
- **ConnectionMapping**: Maps session IDs ↔ SignalR connection IDs. Used by `HostPartyController` to target individual guests for `CreditsUpdated` and `PartyEnded` broadcasts.

## API Route Map

| Prefix | Auth | Purpose |
|---|---|---|
| `/api/party/*` | Session cookie | Guest endpoints (join, state) |
| `/api/host/*` | HostAuth cookie | Host party management (create, end, settings) |
| `/api/queue/*` | Session (add/vote), HostAuth (remove/reorder) | Queue operations |
| `/api/playback/*` | HostAuth cookie | Playback control (play, pause, skip, seek, volume) |
| `/api/search` | Participant (host or joined guest) | Spotify search |
| `/api/auth/*` | *(none)* | Spotify OAuth flow |
| `/hubs/party` | Session cookie | SignalR hub |

## Frontend Component Map

| File | Responsibility |
|---|---|
| `PartyContext.tsx` | Single source of truth, SignalR lifecycle, visibility refresh |
| `NowPlaying.tsx` | RAF-based progress bar, seeking, quip generation |
| `QueueList.tsx` | Voting (optimistic updates), drag-and-drop reorder (host only) |
| `SearchOverlay.tsx` / `useSearch.ts` | Debounced search with request cancellation |
| `HostControls.tsx` | Playback buttons, volume slider (debounced 300ms) |
| `ManagePanel.tsx` | Guest list, credits, kick, end party |
| `DeviceSelector.tsx` | Spotify device picker |
| `BasePlaylistSelector.tsx` | Base playlist picker |
| `HelpOverlay.tsx` | Voting rules explanation for guests |
