# JukeVox

Collaborative music queue powered by Spotify. One person hosts a party, everyone else joins to add songs and vote on what plays next.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
- [mkcert](https://github.com/FiloSottile/mkcert) (for TLS in development)
- A [Spotify Developer](https://developer.spotify.com/dashboard) application

## Setup

### 1. TLS certificate

Both the backend and frontend dev server use a shared TLS certificate. Generate one with mkcert for your custom dev domain:

```sh
mkcert -install   # one-time: trust the local CA
mkcert -cert-file cert.pem -key-file key.pem jukevox-dev.scottp.dev localhost 127.0.0.1
```

Place `cert.pem` and `key.pem` in the project root. Both Vite and Kestrel auto-detect them from there — no extra configuration needed.

### 2. DNS

Point your dev domain to localhost. Add to `/etc/hosts`:

```
127.0.0.1 jukevox-dev.scottp.dev
```

### 3. Spotify app

Create a [Spotify Developer](https://developer.spotify.com/dashboard) application.

**Redirect URI** — add this to your Spotify app's settings:

```
https://jukevox-dev.scottp.dev:5001/api/auth/callback
```

The app requests the following scopes during OAuth:

| Scope | Purpose |
|---|---|
| `user-read-playback-state` | Poll current track and progress |
| `user-modify-playback-state` | Play, pause, skip, seek, volume |
| `user-read-currently-playing` | Detect foreign track changes |
| `streaming` | Web playback SDK (reserved) |
| `playlist-read-private` | Base playlist selection |

### 4. Environment variables

Copy the example file and fill in your values:

```sh
cp .env.example .env
```

Edit `.env`:

```sh
SPOTIFY__ClientId=your_client_id
SPOTIFY__ClientSecret=your_client_secret
SPOTIFY__RedirectUri=https://jukevox-dev.scottp.dev:5001/api/auth/callback

ASPNETCORE_ENVIRONMENT=Development
FRONTENDURL=https://jukevox-dev.scottp.dev:5173

# WebAuthn relying party
HOSTAUTH__ServerDomain=jukevox-dev.scottp.dev
HOSTAUTH__Origins=https://jukevox-dev.scottp.dev:5173
```

When `cert.pem`/`key.pem` are present, Kestrel auto-configures HTTPS on port 5001 so `ASPNETCORE_URLS` is not needed. If the cert files are absent, set `ASPNETCORE_URLS=https://127.0.0.1:5001` explicitly.

Export the variables before running (or use [direnv](https://direnv.net/)):

```sh
export $(cat .env | xargs)
```

**Rider:** In your Run/Debug Configuration, click the browse button on the **Environment variables** field and use **Load from file** to select your `.env`.

### 5. Install dependencies

```sh
cd src/JukeVox.Client && npm install
```

## Running locally

Start both the backend and frontend in separate terminals:

```sh
# Terminal 1 — API server (https://jukevox-dev.scottp.dev:5001)
cd src/JukeVox.Server && dotnet run

# Terminal 2 — Dev server (https://jukevox-dev.scottp.dev:5173)
cd src/JukeVox.Client && npm run dev
```

Open https://jukevox-dev.scottp.dev:5173 in your browser. The Vite dev server proxies `/api` and `/hubs` requests (including WebSocket upgrades for SignalR) to the backend.

### Host setup

1. Set the `JUKEVOX_SETUP_TOKEN` env var to a secret of your choice
2. Navigate to `/host/setup` and enter the token to register your passkey
3. After registration, go to `/host` to log in and manage parties

The passkey credential is stored in `host-credential.json` (gitignored). Host auth cookies are ephemeral — they are invalidated when the server restarts, so you'll need to log in again after each restart.

## Pre-commit hooks

This project uses [pre-commit](https://pre-commit.com/) to catch issues before they're committed. Install the hooks after cloning:

```sh
pre-commit install
```

Hooks include:
- **gitleaks** — scans for secrets, API keys, and tokens
- **detect-private-key** — catches committed private keys
- **trailing-whitespace / end-of-file-fixer** — file hygiene
- **check-merge-conflict** — catches unresolved conflict markers
- **check-added-large-files** — blocks files over 500KB
- **check-json / check-yaml** — validates config syntax

Run against all files manually:

```sh
pre-commit run --all-files
```

## Running tests

```sh
dotnet test JukeVox.slnx
```

Tests use NUnit 4, FluentAssertions, and Moq. Test project is at `tests/JukeVox.Server.Tests/`.

## Architecture

```
src/
  JukeVox.Server/     ASP.NET Core Web API + SignalR hub
  JukeVox.Client/     React 19 + TypeScript (Vite)
tests/
  JukeVox.Server.Tests/
```

- **Queue management** — App-managed queue (not Spotify's) so users can reorder, remove, and vote on tracks. A 4-tier sort system promotes highly-voted songs and demotes disliked ones.
- **Real-time updates** — SignalR pushes queue changes, playback state, credits, and party lifecycle events to all connected clients.
- **Playback monitoring** — A background service polls Spotify every 2 seconds, auto-advances the queue when a track ends, and detects when someone changes the track outside the app.
- **Host auth** — Passkey (WebAuthn) authentication for the host portal via Fido2NetLib v4.
- **Sessions** — Cookie-based, no user accounts required for guests.

For detailed architecture docs (service internals, queue sorting tiers, voting thresholds, gotchas), see [CLAUDE.md](CLAUDE.md).

## Production

### Docker

The project includes a multi-stage Dockerfile:

1. **Frontend build** — `node:22-alpine` runs `npm ci && npm run build`, outputting to `src/Jukevox.Server/wwwroot/`
2. **Backend build** — `dotnet/sdk:10.0-alpine` restores and publishes the server project
3. **Runtime** — `dotnet/aspnet:10.0-alpine` runs the app on port 8080

Build and run locally:

```sh
docker build -t jukevox .
docker run -p 8080:8080 \
  -e SPOTIFY__ClientId=... \
  -e SPOTIFY__ClientSecret=... \
  -e SPOTIFY__RedirectUri=... \
  -e FRONTENDURL=... \
  -e HOSTAUTH__ServerDomain=... \
  -e HOSTAUTH__Origins=... \
  jukevox
```

In production, the backend serves the built React app as static files with SPA fallback (`MapFallbackToFile("index.html")`), so no separate frontend server is needed.

The container runs as a non-root user (`jukevox`, UID 10000). The health endpoint is `GET /api/health`.

### CI/CD

Pipelines are in `.woodpecker/`, running on a self-hosted [Woodpecker CI](https://woodpecker-ci.org/) instance.

- **Build** — triggers on push to `main`. Builds the Docker image, scans it for vulnerabilities with [Grype](https://github.com/anchore/grype) (fails on critical findings), then pushes to an internal container registry.
- **Deploy** — manual trigger. Updates the Kubernetes manifest in a separate GitOps repo, which ArgoCD syncs automatically.


## Persisted state

Two files persist across restarts (both gitignored):

| File | Contents | Notes |
|---|---|---|
| `party-state.json` | Active party, queue, guests, tokens | Written after every state mutation by `PartyService` |
| `host-credential.json` | WebAuthn public key credential | Created during passkey registration at `/host/setup` |

Host auth cookies are **not** persisted — they use ephemeral Data Protection keys that are lost on restart. The host will need to re-authenticate via passkey after each server restart.
