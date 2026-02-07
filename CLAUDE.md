# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Build & Run

```sh
# Build
dotnet build Jukevox.slnx

# Run tests
dotnet test Jukevox.slnx

# Run backend (https://127.0.0.1:5001)
cd src/Jukevox.Server && dotnet run

# Run frontend dev server (http://localhost:5173)
cd src/Jukevox.Client && npm run dev

# Lint frontend
cd src/Jukevox.Client && npm run lint
```

Spotify credentials are provided via environment variables (`SPOTIFY__ClientId`, `SPOTIFY__ClientSecret`, `SPOTIFY__RedirectUri`). See `.env.example`.

## Architecture

Jukevox is a collaborative Spotify queue app. A host creates a party, connects their Spotify account, and guests join via invite code to add songs.

**Backend:** .NET 10 ASP.NET Core Web API + SignalR (`src/Jukevox.Server/`)
**Frontend:** React 19 + TypeScript + Vite (`src/Jukevox.Client/`)
**Tests:** xUnit (`tests/Jukevox.Server.Tests/`)

### Backend request flow

1. `PartySessionMiddleware` extracts/creates a session ID from the `Jukevox.SessionId` cookie, stores it in `HttpContext.Items["SessionId"]`
2. Controllers access it via `HttpContext.GetSessionId()` extension method
3. Controllers delegate to singleton services (`PartyService`, `QueueService`) for state mutations
4. After mutations, controllers broadcast updates to clients via `IHubContext<PartyHub, IPartyClient>`

### Key services

- **PartyService** — Singleton. Owns the single active `Party` object. All public methods acquire a `Lock` before reading/writing state. Persists state to `party-state.json` after every mutation.
- **QueueService** — Singleton. Manages queue add/remove/reorder/dequeue. Also lock-synchronized. Injects `IHubContext` to broadcast `QueueUpdated` after changes.
- **PlaybackMonitorService** — `BackgroundService` that polls Spotify every 2 seconds. Detects track endings, skips, and foreign queue items. Auto-advances the queue and broadcasts `NowPlayingChanged`/`PlaybackStateUpdated` via SignalR.
- **SpotifyAuthService** — Handles OAuth authorization code flow and token refresh. Tokens are stored in the `Party` model and auto-refreshed with a 1-minute expiry buffer.

### Thread safety

`PartyService` and `QueueService` are singletons using C# 13 `Lock` (not `object` locks). Every public method that touches state must acquire the lock.

### SignalR

`PartyHub` groups clients by party ID. Server-to-client messages are defined in `IPartyClient`: `NowPlayingChanged`, `PlaybackStateUpdated`, `QueueUpdated`, `CreditsUpdated`. Services broadcast via injected `IHubContext<PartyHub, IPartyClient>`.

### Frontend state management

`PartyContext` (React Context) is the single source of truth. On mount it checks for an existing session via `GET /api/party/state`. When a party is active, it creates a SignalR connection that updates `nowPlaying`, `queue`, and `credits` in real time. REST calls go through `src/api/client.ts` with `credentials: 'include'` for cookie auth.

### Vite proxy

The frontend dev server proxies `/api/*` and `/hubs/*` (including WebSocket upgrades) to `https://127.0.0.1:5001`.
