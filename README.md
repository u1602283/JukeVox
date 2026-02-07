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

Place `cert.pem` and `key.pem` in the project root. Both Vite and Kestrel auto-detect them from there.

### 2. DNS

Point your dev domain to localhost. Add to `/etc/hosts`:

```
127.0.0.1 jukevox-dev.scottp.dev
```

### 3. Spotify app

Create a [Spotify Developer](https://developer.spotify.com/dashboard) application and add the redirect URI:

```
https://jukevox-dev.scottp.dev:5001/api/auth/callback
```

### 4. Environment variables

Copy the example file and fill in your values:

```sh
cp .env.example .env
```

Edit `.env`:

```
SPOTIFY__ClientId=your_client_id
SPOTIFY__ClientSecret=your_client_secret
SPOTIFY__RedirectUri=https://jukevox-dev.scottp.dev:5001/api/auth/callback

ASPNETCORE_ENVIRONMENT=Development
FRONTENDURL=https://jukevox-dev.scottp.dev:5173

# WebAuthn relying party
HOSTAUTH__ServerDomain=jukevox-dev.scottp.dev
HOSTAUTH__Origins=https://jukevox-dev.scottp.dev:5173
```

When `cert.pem`/`key.pem` are present, Kestrel auto-configures HTTPS on port 5001 so `ASPNETCORE_URLS` is not needed. If the cert files are absent, set `ASPNETCORE_URLS` explicitly.

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

Open https://jukevox-dev.scottp.dev:5173 in your browser. The Vite dev server proxies `/api` and `/hubs` requests to the backend.

### Host setup

1. Set the `JUKEVOX_SETUP_TOKEN` env var to a secret of your choice
2. Navigate to `/host/setup` and enter the token to register your passkey
3. After registration, go to `/host` to log in and manage parties

Host auth cookies are ephemeral — they are invalidated when the server restarts, so you'll need to log in again after each restart.

## Running tests

```sh
dotnet test JukeVox.slnx
```

## Architecture

```
src/
  JukeVox.Server/     ASP.NET Core Web API + SignalR hub
  JukeVox.Client/     React + TypeScript (Vite)
tests/
  JukeVox.Server.Tests/
```

- **Queue management** — App-managed queue (not Spotify's) so users can reorder and remove tracks.
- **Real-time updates** — SignalR pushes queue and playback state changes to all connected clients.
- **Playback monitoring** — A background service polls Spotify every 2 seconds and auto-advances the queue when a track ends.
- **Host auth** — Passkey (WebAuthn) authentication for the host portal via Fido2NetLib.
- **Sessions** — Cookie-based, no user accounts required for guests.

## To-do

- Redesign the UI
- Build out host functionalities - e.g. guest management (credits etc.)
- Build pretty playback interface
- Dockerise
