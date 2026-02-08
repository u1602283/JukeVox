import type {
  PartyState,
  CreatePartyRequest,
  JoinPartyRequest,
  SearchResult,
  QueueItem,
  AddToQueueRequest,
  SpotifyDevice,
  SpotifyPlaylist,
  SavedPartySummary,
  HostStatus,
  GuestInfo,
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

    // On auth failure, redirect to the appropriate starting page
    if ((res.status === 401 || res.status === 403) &&
        !url.includes('/api/host/setup/') && !url.includes('/api/host/login/')) {
      const isHostPage = window.location.pathname.startsWith('/host');
      window.location.href = isHostPage ? '/host' : '/';
    }

    throw new Error(body.error || `Request failed: ${res.status}`);
  }
  const text = await res.text();
  if (!text) return undefined as T;
  return JSON.parse(text);
}

export const api = {
  // Host auth
  hostStatus: () =>
    request<HostStatus>(`${BASE}/host/status`),

  hostSetupStatus: () =>
    request<{ available: boolean }>(`${BASE}/host/setup/status`),

  hostSetupBegin: (token: string) =>
    request<Record<string, unknown>>(`${BASE}/host/setup/begin`, {
      method: 'POST',
      body: JSON.stringify({ token }),
    }),

  hostSetupComplete: (attestation: unknown) =>
    request<{ success: boolean; dnsRecord: string }>(`${BASE}/host/setup/complete`, {
      method: 'POST',
      body: JSON.stringify(attestation),
    }),

  hostLoginBegin: () =>
    request<Record<string, unknown>>(`${BASE}/host/login/begin`, { method: 'POST' }),

  hostLoginComplete: (assertion: unknown) =>
    request<{ success: boolean }>(`${BASE}/host/login/complete`, {
      method: 'POST',
      body: JSON.stringify(assertion),
    }),

  hostLogout: () =>
    request<{ success: boolean }>(`${BASE}/host/logout`, { method: 'POST' }),

  // Host party management
  createParty: (data: CreatePartyRequest) =>
    request<PartyState>(`${BASE}/host/party`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  resumeParty: () =>
    request<PartyState>(`${BASE}/host/party/resume`, { method: 'POST' }),

  getSavedParty: () =>
    request<SavedPartySummary>(`${BASE}/host/party/saved`),

  updateSettings: (data: { inviteCode?: string; defaultCredits?: number }) =>
    request<void>(`${BASE}/host/party/settings`, {
      method: 'PUT',
      body: JSON.stringify(data),
    }),

  getPlaylists: (limit = 50, offset = 0) =>
    request<SpotifyPlaylist[]>(`${BASE}/host/party/playlists?limit=${limit}&offset=${offset}`),

  setBasePlaylist: (playlistId: string) =>
    request<{ queue: QueueItem[]; basePlaylistId: string; basePlaylistName: string }>(
      `${BASE}/host/party/base-playlist`,
      { method: 'PUT', body: JSON.stringify({ playlistId }) },
    ),

  clearBasePlaylist: () =>
    request<{ queue: QueueItem[] }>(`${BASE}/host/party/base-playlist`, { method: 'DELETE' }),

  getGuests: () =>
    request<GuestInfo[]>(`${BASE}/host/party/guests`),

  setGuestCredits: (sessionId: string, credits: number) =>
    request<GuestInfo>(`${BASE}/host/party/guests/${encodeURIComponent(sessionId)}/credits`, {
      method: 'PUT',
      body: JSON.stringify({ credits }),
    }),

  addCreditsToAll: (credits: number) =>
    request<GuestInfo[]>(`${BASE}/host/party/guests/credits`, {
      method: 'POST',
      body: JSON.stringify({ credits }),
    }),

  endParty: () =>
    request<{ ended: boolean }>(`${BASE}/host/party/end`, { method: 'POST' }),

  // Guest party
  joinParty: (data: JoinPartyRequest) =>
    request<PartyState>(`${BASE}/party/join`, {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  getPartyState: () =>
    request<PartyState & { hasParty?: boolean }>(`${BASE}/party/state`),

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
  previous: (progressMs: number) =>
    request<void>(`${BASE}/playback/previous?progressMs=${progressMs}`, { method: 'POST' }),
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
