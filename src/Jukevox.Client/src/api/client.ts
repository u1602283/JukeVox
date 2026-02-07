import type {
  PartyState,
  CreatePartyRequest,
  JoinPartyRequest,
  SearchResult,
  QueueItem,
  AddToQueueRequest,
  SpotifyDevice,
  SpotifyPlaylist,
  PlaybackState,
  SavedPartySummary,
} from '../types';

const BASE = '/api';

async function request<T>(url: string, options?: RequestInit): Promise<T> {
  const res = await fetch(url, {
    credentials: 'include',
    headers: { 'Content-Type': 'application/json' },
    ...options,
  });
  if (!res.ok) {
    const body = await res.json().catch(() => ({}));
    throw new Error(body.error || `Request failed: ${res.status}`);
  }
  const text = await res.text();
  if (!text) return undefined as T;
  return JSON.parse(text);
}

export const api = {
  // Party
  createParty: (data: CreatePartyRequest) =>
    request<PartyState>(`${BASE}/party`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  joinParty: (data: JoinPartyRequest) =>
    request<PartyState>(`${BASE}/party/join`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  getPartyState: () =>
    request<PartyState & { hasParty?: boolean }>(`${BASE}/party/state`),

  getSavedParty: () =>
    request<SavedPartySummary>(`${BASE}/party/saved`),

  resumeParty: () =>
    request<PartyState>(`${BASE}/party/resume`, { method: 'POST' }),

  updateSettings: (data: { inviteCode?: string; defaultCredits?: number }) =>
    request<void>(`${BASE}/party/settings`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  getPlaylists: (limit = 50, offset = 0) =>
    request<SpotifyPlaylist[]>(`${BASE}/party/playlists?limit=${limit}&offset=${offset}`),

  setBasePlaylist: (playlistId: string) =>
    request<{ queue: QueueItem[]; basePlaylistId: string; basePlaylistName: string }>(
      `${BASE}/party/base-playlist`,
      { method: 'PUT', body: JSON.stringify({ playlistId }) },
    ),

  clearBasePlaylist: () =>
    request<{ queue: QueueItem[] }>(`${BASE}/party/base-playlist`, { method: 'DELETE' }),

  // Auth
  getAuthStatus: () =>
    request<{ connected: boolean; isExpired: boolean }>(`${BASE}/auth/status`),

  // Search
  search: (q: string, limit = 20) =>
    request<SearchResult[]>(`${BASE}/search?q=${encodeURIComponent(q)}&limit=${limit}`),

  // Queue
  getQueue: () => request<QueueItem[]>(`${BASE}/queue`),

  addToQueue: (data: AddToQueueRequest) =>
    request<{ queue: QueueItem[]; creditsRemaining?: number }>(`${BASE}/queue`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  removeFromQueue: (id: string) =>
    request<QueueItem[]>(`${BASE}/queue/${id}`, { method: 'DELETE' }),

  reorderQueue: (orderedIds: string[]) =>
    request<QueueItem[]>(`${BASE}/queue/reorder`, {
      method: 'PUT',
      body: JSON.stringify({ orderedIds }),
    }),

  // Playback
  pause: () => request<void>(`${BASE}/playback/pause`, { method: 'POST' }),
  resume: () => request<void>(`${BASE}/playback/resume`, { method: 'POST' }),
  previous: () => request<void>(`${BASE}/playback/previous`, { method: 'POST' }),
  skip: () => request<void>(`${BASE}/playback/skip`, { method: 'POST' }),
  seek: (positionMs: number) =>
    request<void>(`${BASE}/playback/seek?positionMs=${positionMs}`, { method: 'PUT' }),

  setVolume: (percent: number) =>
    request<void>(`${BASE}/playback/volume?percent=${percent}`, { method: 'PUT' }),

  getDevices: () => request<SpotifyDevice[]>(`${BASE}/playback/devices`),

  selectDevice: (deviceId: string) =>
    request<void>(`${BASE}/playback/device`, {
      method: 'PUT',
      body: JSON.stringify({ deviceId }),
    }),
};
