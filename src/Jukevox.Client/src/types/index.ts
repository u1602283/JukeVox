export interface PartyState {
  partyId: string;
  inviteCode: string;
  isHost: boolean;
  spotifyConnected: boolean;
  creditsRemaining?: number;
  displayName?: string;
  defaultCredits: number;
  queue: QueueItem[];
  nowPlaying?: PlaybackState;
  basePlaylistId?: string;
  basePlaylistName?: string;
}

export interface QueueItem {
  id: string;
  trackUri: string;
  trackName: string;
  artistName: string;
  albumName: string;
  albumImageUrl?: string;
  durationMs: number;
  addedByName: string;
  addedAt: string;
  isFromBasePlaylist: boolean;
}

export interface PlaybackState {
  isPlaying: boolean;
  trackName?: string;
  artistName?: string;
  albumName?: string;
  albumImageUrl?: string;
  progressMs: number;
  durationMs: number;
  volumePercent: number;
  deviceId?: string;
  deviceName?: string;
}

export interface SearchResult {
  trackUri: string;
  trackName: string;
  artistName: string;
  albumName: string;
  albumImageUrl?: string;
  durationMs: number;
}

export interface SpotifyDevice {
  id: string;
  name: string;
  type: string;
  isActive: boolean;
  volumePercent: number;
}

export interface CreatePartyRequest {
  inviteCode?: string;
  defaultCredits: number;
}

export interface JoinPartyRequest {
  inviteCode: string;
  displayName: string;
}

export interface SavedPartySummary {
  exists: boolean;
  inviteCode?: string;
  queueCount: number;
  guestCount: number;
  createdAt?: string;
}

export interface SpotifyPlaylist {
  id: string;
  name: string;
  imageUrl?: string;
  trackCount: number;
}

export interface AddToQueueRequest {
  trackUri: string;
  trackName: string;
  artistName: string;
  albumName: string;
  albumImageUrl?: string;
  durationMs: number;
}

export interface HostStatus {
  authenticated: boolean;
  hasCredential: boolean;
  setupAvailable: boolean;
}
