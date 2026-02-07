# Jukevox

Collaborative music queue powered by Spotify. One person hosts a party, everyone else joins to add songs and vote on what plays next.

## Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Node.js 22+](https://nodejs.org/)
- A [Spotify Developer](https://developer.spotify.com/dashboard) application with a redirect URI set to `https://127.0.0.1:5001/api/auth/callback`

## Setup

### Spotify credentials

The backend reads Spotify credentials from environment variables. Copy the example file and fill in your values:

```sh
cp .env.example .env
```

Then edit `.env` with your Spotify app's client ID and secret:

```
SPOTIFY__ClientId=your_client_id
SPOTIFY__ClientSecret=your_client_secret
SPOTIFY__RedirectUri=https://127.0.0.1:5001/api/auth/callback
```

Export them before running the server (or use a tool like [direnv](https://direnv.net/)):

```sh
export $(cat .env | xargs)
```

**Rider:** In your Run/Debug Configuration, click the browse button on the **Environment variables** field and use **Load from file** to select your `.env`.

### Install dependencies

```sh
cd src/Jukevox.Client && npm install
```

## Running locally

Start both the backend and frontend in separate terminals:

```sh
# Terminal 1 — API server (https://127.0.0.1:5001)
cd src/Jukevox.Server && dotnet run

# Terminal 2 — Dev server (http://localhost:5173)
cd src/Jukevox.Client && npm run dev
```

Open http://localhost:5173 in your browser. The Vite dev server proxies `/api` and `/hubs` requests to the backend.

## Running tests

```sh
dotnet test Jukevox.slnx
```

## Architecture

```
src/
  Jukevox.Server/     ASP.NET Core Web API + SignalR hub
  Jukevox.Client/     React + TypeScript (Vite)
tests/
  Jukevox.Server.Tests/
```

- **Queue management** — App-managed queue (not Spotify's) so users can reorder and remove tracks.
- **Real-time updates** — SignalR pushes queue and playback state changes to all connected clients.
- **Playback monitoring** — A background service polls Spotify every 2 seconds and auto-advances the queue when a track ends.
- **Sessions** — Cookie-based, no user accounts required.
