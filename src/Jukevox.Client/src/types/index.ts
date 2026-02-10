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
  userVotes?: Record<string, number>;
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
  score: number;
}

export interface PlaybackState {
  isPlaying: boolean;
  trackUri?: string;
  trackName?: string;
  artistName?: string;
  albumName?: string;
  albumImageUrl?: string;
  progressMs: number;
  durationMs: number;
  volumePercent: number;
  supportsVolume: boolean;
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
  supportsVolume: boolean;
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

export interface GuestInfo {
  sessionId: string;
  displayName: string;
  creditsRemaining: number;
  joinedAt: string;
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
