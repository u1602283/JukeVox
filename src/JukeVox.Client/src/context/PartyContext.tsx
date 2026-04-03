import { createContext, useState, useCallback, useEffect, useRef } from 'react';
import type { ReactNode } from 'react';
import type { PartyState, PlaybackState, QueueItem } from '../types';
import { api } from '../api/client';
import { createPartyConnection, startConnection, stopConnection, ensureConnected } from '../signalr/partyConnection';

export interface PartyContextValue {
  party: PartyState | null;
  setParty: (party: PartyState | null) => void;
  nowPlaying: PlaybackState | null;
  queue: QueueItem[];
  setQueue: (queue: QueueItem[] | ((prev: QueueItem[]) => QueueItem[])) => void;
  credits: number | null;
  setCredits: (credits: number) => void;
  error: string | null;
  setError: (error: string | null) => void;
  loading: boolean;
  userVotes: Record<string, number>;
  setUserVote: (itemId: string, vote: number) => void;
  isSleeping: boolean;
}

// eslint-disable-next-line react-refresh/only-export-components
export const PartyCtx = createContext<PartyContextValue | null>(null);

export function PartyProvider({ children }: { children: ReactNode }) {
  const [party, setPartyRaw] = useState<PartyState | null>(null);
  const [nowPlaying, setNowPlaying] = useState<PlaybackState | null>(null);
  const [queue, setQueue] = useState<QueueItem[]>([]);
  const [credits, setCredits] = useState<number | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(true);
  const [userVotes, setUserVotes] = useState<Record<string, number>>({});
  const [isSleeping, setIsSleeping] = useState(false);
  const connectedRef = useRef(false);

  const setParty = useCallback((state: PartyState | null) => {
    setPartyRaw(state);
    if (state) {
      if (state.queue) setQueue(state.queue);
      if (state.nowPlaying !== undefined) setNowPlaying(state.nowPlaying || null);
      if (state.creditsRemaining !== undefined) setCredits(state.creditsRemaining);
      if (state.userVotes) setUserVotes(state.userVotes);
      setIsSleeping(state.isSleeping ?? false);
    }
  }, []);

  const setUserVote = useCallback((itemId: string, vote: number) => {
    setUserVotes(prev => {
      if (vote === 0) {
        const { [itemId]: _removed, ...rest } = prev;
        void _removed;
        return rest;
      }
      return { ...prev, [itemId]: vote };
    });
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

  // Refetch state when the page becomes visible again (e.g. phone unlock)
  const partyRef = useRef(party);
  partyRef.current = party;
  useEffect(() => {
    const onVisibilityChange = () => {
      if (document.visibilityState !== 'visible' || !partyRef.current) return;

      api.getPartyState()
        .then((state) => {
          if ('hasParty' in state && !state.hasParty) {
            setParty(null);
            return;
          }
          const s = state as PartyState;
          if (s.queue) setQueue(s.queue);
          if (s.nowPlaying !== undefined) setNowPlaying(s.nowPlaying || null);
          if (s.creditsRemaining !== undefined) setCredits(s.creditsRemaining);
          if (s.userVotes) setUserVotes(s.userVotes);
          setIsSleeping(s.isSleeping ?? false);
        })
        .catch(() => { /* keep existing state on error */ });

      // Ensure SignalR is alive after sleep
      ensureConnected().catch(() => { /* will retry on next poll */ });
    };
    document.addEventListener('visibilitychange', onVisibilityChange);
    return () => document.removeEventListener('visibilitychange', onVisibilityChange);
  }, [setParty]);

  // Connect SignalR when party is active
  useEffect(() => {
    if (!party || connectedRef.current) return;

    const conn = createPartyConnection(party.partyId, {
      onNowPlayingChanged: (state) => { setNowPlaying(state); },
      onPlaybackStateUpdated: (state) => { setNowPlaying(state); },
      onQueueUpdated: (q) => {
        setQueue(q);
        // Prune stale votes for items no longer in the queue
        const queueIds = new Set(q.map(item => item.id));
        setUserVotes(prev => {
          const pruned: Record<string, number> = {};
          for (const [id, vote] of Object.entries(prev)) {
            if (queueIds.has(id)) pruned[id] = vote;
          }
          return pruned;
        });
      },
      onCreditsUpdated: (c) => { setCredits(c); },
      onPartyEnded: () => {
        setPartyRaw(null);
        setNowPlaying(null);
        setQueue([]);
        setCredits(null);
        setUserVotes({});
        setIsSleeping(false);
        // Navigate guests to landing page
        if (!party.isHost && window.location.pathname !== '/host') {
          window.location.href = '/';
        }
      },
      onPartySleeping: () => { setIsSleeping(true); },
      onPartyWoke: () => { setIsSleeping(false); },
    });

    conn.onclose(() => {
      connectedRef.current = false;
    });

    startConnection()
      .then(() => { connectedRef.current = true; })
      .catch((err) => console.error('SignalR connection failed:', err));

    return () => {
      connectedRef.current = false;
      stopConnection();
    };
  }, [party, setParty]);

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
    userVotes,
    setUserVote,
    isSleeping,
  };

  return <PartyCtx.Provider value={value}>{children}</PartyCtx.Provider>;
}
