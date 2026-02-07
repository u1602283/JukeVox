import { createContext, useContext, useState, useCallback, useEffect, useRef, useMemo } from 'react';
import type { ReactNode } from 'react';
import type { PartyState, PlaybackState, QueueItem } from '../types';
import { api } from '../api/client';
import { createPartyConnection, startConnection, stopConnection } from '../signalr/partyConnection';

interface PartyContextValue {
  party: PartyState | null;
  setParty: (party: PartyState | null) => void;
  nowPlaying: PlaybackState | null;
  queue: QueueItem[];
  setQueue: (queue: QueueItem[]) => void;
  credits: number | null;
  setCredits: (credits: number) => void;
  error: string | null;
  setError: (error: string | null) => void;
  loading: boolean;
}

const PartyCtx = createContext<PartyContextValue | null>(null);

export function PartyProvider({ children }: { children: ReactNode }) {
  const [party, setPartyRaw] = useState<PartyState | null>(null);
  const [nowPlaying, setNowPlaying] = useState<PlaybackState | null>(null);
  const [queue, setQueue] = useState<QueueItem[]>([]);
  const [credits, setCredits] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const connectedRef = useRef(false);

  const setParty = useCallback((state: PartyState | null) => {
    setPartyRaw(state);
    if (state) {
      if (state.queue) setQueue(state.queue);
      if (state.nowPlaying !== undefined) setNowPlaying(state.nowPlaying || null);
      if (state.creditsRemaining !== undefined) setCredits(state.creditsRemaining);
    }
  }, []);

  // On mount, check for existing party session
  useEffect(() => {
    api.getPartyState()
      .then((state) => {
        if ('hasParty' in state && !state.hasParty) {
          setParty(null);
        } else {
          setParty(state as PartyState);
        }
      })
      .catch(() => setParty(null))
      .finally(() => setLoading(false));
  }, [setParty]);

  // Connect SignalR when party is active
  useEffect(() => {
    if (!party || connectedRef.current) return;

    const conn = createPartyConnection(party.partyId, {
      onNowPlayingChanged: (state) => setNowPlaying(state),
      onPlaybackStateUpdated: (state) => setNowPlaying(state),
      onQueueUpdated: (q) => setQueue(q),
      onCreditsUpdated: (c) => setCredits(c),
    });

    startConnection()
      .then(() => { connectedRef.current = true; })
      .catch((err) => console.error('SignalR connection failed:', err));

    return () => {
      connectedRef.current = false;
      stopConnection();
    };
  }, [party]);

  const value: PartyContextValue = {
    party,
    setParty,
    nowPlaying,
    queue,
    setQueue,
    credits,
    setCredits,
    error,
    setError,
    loading,
  };

  return <PartyCtx.Provider value={value}>{children}</PartyCtx.Provider>;
}

export function useParty(): PartyContextValue {
  const ctx = useContext(PartyCtx);
  if (!ctx) throw new Error('useParty must be used within PartyProvider');
  return ctx;
}
