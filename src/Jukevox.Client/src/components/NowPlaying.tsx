import type { ReactNode } from 'react';
import { useEffect, useRef, useMemo } from 'react';
import { Music } from 'lucide-react';
import { api } from '../api/client';
import { useParty } from '../hooks/useParty';
import styles from './NowPlaying.module.css';

function formatTime(ms: number): string {
  const mins = Math.floor(ms / 60000);
  const secs = Math.floor((ms % 60000) / 1000);
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}

const guestQuips = [
  (n: string) => `Queued by ${n}. Finger snaps for ${n}, everyone!`,
  (n: string) => `Blame ${n} for this one.`,
  (n: string) => `${n} spent one of their precious credits on this. No refunds.`,
  (n: string) => `This is ${n}'s fault. Take it up with them.`,
  (n: string) => `${n} would like everyone to know they have taste. Allegedly.`,
  (n: string) => `A gift to the party from ${n}. Keep the receipt.`,
  (n: string) => `${n} heard this song once and made it everyone's problem.`,
  (n: string) => `${n} queued this with their whole chest.`,
  (n: string) => `Everyone thank ${n}. Or don't. It's a free country.`,
  (n: string) => `Personally selected by ${n}. Condolences or congratulations as appropriate.`,
  (n: string) => `${n} really said "this is the one" and hit queue. Confidence is key.`,
  (n: string) => `DJ ${n} has entered the chat.`,
  (n: string) => `${n} chose this. Bold. Very bold.`,
];

const basePlaylistQuips = [
  "Nobody queued this. The algorithm is in charge now. Happy?",
  "Auto-playing because apparently nobody has opinions.",
  "This is what happens when nobody queues. You did this.",
  "The playlist is on autopilot. Feel free to actually participate anytime.",
  "Playing from the backup playlist. Your silence has been noted.",
  "Chosen by a computer because humans couldn't be bothered.",
  "Nobody had anything to say so here we are.",
  "The queue was empty. Nature abhors a vacuum and so does this DJ.",
];

function hashString(s: string): number {
  let h = 0;
  for (let i = 0; i < s.length; i++) {
    h = ((h << 5) - h + s.charCodeAt(i)) | 0;
  }
  return Math.abs(h);
}

function getQuip(addedByName?: string, isFromBasePlaylist?: boolean, trackUri?: string): string | null {
  if (!trackUri) return null;
  const hash = hashString(trackUri);
  if (isFromBasePlaylist || addedByName === 'Base Playlist') {
    return basePlaylistQuips[hash % basePlaylistQuips.length];
  }
  if (addedByName && addedByName !== 'Host') {
    return guestQuips[hash % guestQuips.length](addedByName);
  }
  return null;
}

export function NowPlaying({ children }: { children?: ReactNode }) {
  const { nowPlaying, party } = useParty();
  const isHost = party?.isHost && party.spotifyConnected;

  // Seeking (ref-only, no React state needed)
  const seekingRef = useRef(false);
  const seekMsRef = useRef(0);

  // Interpolation baseline
  const serverProgressRef = useRef(0);
  const lastServerTimeRef = useRef(0);
  const pendingSeekRef = useRef<number | null>(null);

  // DOM refs for direct manipulation (bypasses React re-renders)
  const progressFillRef = useRef<HTMLDivElement>(null);
  const sliderRef = useRef<HTMLInputElement>(null);
  const sliderWrapRef = useRef<HTMLDivElement>(null);
  const elapsedRef = useRef<HTMLSpanElement>(null);

  // Absorb server updates into refs
  useEffect(() => {
    if (!nowPlaying) return;

    if (pendingSeekRef.current !== null) {
      const diff = Math.abs(nowPlaying.progressMs - pendingSeekRef.current);
      if (diff > 3000) return;
      pendingSeekRef.current = null;
    }

    serverProgressRef.current = nowPlaying.progressMs;
    lastServerTimeRef.current = performance.now();
  }, [nowPlaying]);

  // requestAnimationFrame loop — 60fps, zero React re-renders
  useEffect(() => {
    if (!nowPlaying?.trackName) return;

    const duration = nowPlaying.durationMs || 1;
    const playing = nowPlaying.isPlaying;
    let rafId: number;

    const tick = () => {
      let ms: number;
      if (seekingRef.current) {
        ms = seekMsRef.current;
      } else if (playing) {
        const elapsed = performance.now() - lastServerTimeRef.current;
        ms = Math.min(serverProgressRef.current + elapsed, duration);
      } else {
        ms = serverProgressRef.current;
      }

      const frac = Math.min(ms / duration, 1);
      const pct = `${frac * 100}%`;

      // Direct DOM writes — no reconciliation overhead
      if (progressFillRef.current) {
        progressFillRef.current.style.width = pct;
      }
      if (sliderWrapRef.current) {
        sliderWrapRef.current.style.setProperty('--seek-pct', pct);
      }
      if (sliderRef.current && !seekingRef.current) {
        sliderRef.current.value = String(Math.round(ms));
      }
      if (elapsedRef.current) {
        elapsedRef.current.textContent = formatTime(ms);
      }

      rafId = requestAnimationFrame(tick);
    };

    rafId = requestAnimationFrame(tick);
    return () => cancelAnimationFrame(rafId);
  }, [nowPlaying?.isPlaying, nowPlaying?.durationMs, nowPlaying?.trackUri, nowPlaying?.trackName]);

  const quip = useMemo(
    () => getQuip(nowPlaying?.addedByName, nowPlaying?.isFromBasePlaylist, nowPlaying?.trackUri),
    [nowPlaying?.addedByName, nowPlaying?.isFromBasePlaylist, nowPlaying?.trackUri]
  );

  if (!nowPlaying || !nowPlaying.trackName) {
    return (
      <div className={styles.container}>
        <div className={`${styles.content} ${styles.empty}`}>
          <div style={{ display: 'flex', flexDirection: 'column', alignItems: 'center', gap: '8px' }}>
            <Music size={40} className={styles.emptyIcon} />
            <p className={styles.emptyText}>Nothing playing</p>
          </div>
        </div>
      </div>
    );
  }

  const handleSeekStart = () => {
    seekingRef.current = true;
    const elapsed = performance.now() - lastServerTimeRef.current;
    seekMsRef.current = Math.min(
      serverProgressRef.current + elapsed,
      nowPlaying.durationMs
    );
  };

  const handleSeekMove = (e: React.ChangeEvent<HTMLInputElement>) => {
    seekMsRef.current = parseInt(e.target.value, 10);
  };

  const handleSeekEnd = () => {
    const val = seekMsRef.current;
    pendingSeekRef.current = val;
    serverProgressRef.current = val;
    lastServerTimeRef.current = performance.now();
    seekingRef.current = false;
    api.seek(val).catch(() => {});
  };

  const initFrac = nowPlaying.durationMs > 0
    ? Math.min(nowPlaying.progressMs / nowPlaying.durationMs, 1)
    : 0;

  const albumArtStyle = nowPlaying.albumImageUrl
    ? { '--album-art-url': `url(${nowPlaying.albumImageUrl})` } as React.CSSProperties
    : undefined;

  return (
    <div className={styles.container} style={albumArtStyle}>
      <div className={styles.content}>
        {nowPlaying.albumImageUrl && (
          <img
            src={nowPlaying.albumImageUrl}
            alt={nowPlaying.albumName || ''}
            className={styles.art}
          />
        )}
        <div className={styles.progress}>
          {isHost ? (
            <div
              className={styles.seekSlider}
              ref={sliderWrapRef}
              style={{ '--seek-pct': `${initFrac * 100}%` } as React.CSSProperties}
            >
              <input
                ref={sliderRef}
                type="range"
                min={0}
                max={nowPlaying.durationMs || 1}
                defaultValue={Math.round(nowPlaying.progressMs)}
                onPointerDown={handleSeekStart}
                onChange={handleSeekMove}
                onPointerUp={handleSeekEnd}
              />
            </div>
          ) : (
            <div className={styles.progressBar}>
              <div
                ref={progressFillRef}
                className={styles.progressFill}
                style={{ width: `${initFrac * 100}%` }}
              />
            </div>
          )}
          <div className={styles.times}>
            <span ref={elapsedRef}>{formatTime(nowPlaying.progressMs)}</span>
            <span>{formatTime(nowPlaying.durationMs)}</span>
          </div>
        </div>
        <div className={styles.info}>
          <div className={styles.track}>{nowPlaying.trackName}</div>
          <div className={styles.artist}>{nowPlaying.artistName}</div>
          {quip && <div className={styles.quip}>{quip}</div>}
        </div>
        {children}
      </div>
    </div>
  );
}
