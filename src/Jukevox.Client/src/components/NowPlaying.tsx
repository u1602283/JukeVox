import { useEffect, useRef } from 'react';
import { api } from '../api/client';
import { useParty } from '../context/PartyContext';

function formatTime(ms: number): string {
  const mins = Math.floor(ms / 60000);
  const secs = Math.floor((ms % 60000) / 1000);
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}

export function NowPlaying() {
  const { nowPlaying, party } = useParty();
  const isHost = party?.isHost && party.spotifyConnected;

  // Seeking (ref-only, no React state needed)
  const seekingRef = useRef(false);
  const seekMsRef = useRef(0);

  // Interpolation baseline
  const serverProgressRef = useRef(0);
  const lastServerTimeRef = useRef(performance.now());
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
  }, [nowPlaying?.progressMs, nowPlaying?.isPlaying, nowPlaying?.trackUri]);

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

  if (!nowPlaying || !nowPlaying.trackName) {
    return (
      <div className="now-playing empty">
        <p>Nothing playing</p>
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

  return (
    <div className="now-playing">
      {nowPlaying.albumImageUrl && (
        <img
          src={nowPlaying.albumImageUrl}
          alt={nowPlaying.albumName || ''}
          className="now-playing-art"
        />
      )}
      <div className="now-playing-info">
        <div className="now-playing-track">{nowPlaying.trackName}</div>
        <div className="now-playing-artist">{nowPlaying.artistName}</div>

        {isHost ? (
          <div
            className="seek-slider"
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
          <div className="progress-bar">
            <div
              ref={progressFillRef}
              className="progress-fill"
              style={{ width: `${initFrac * 100}%` }}
            />
          </div>
        )}

        <div className="progress-times">
          <span ref={elapsedRef}>{formatTime(nowPlaying.progressMs)}</span>
          <span>{formatTime(nowPlaying.durationMs)}</span>
        </div>
      </div>
    </div>
  );
}
