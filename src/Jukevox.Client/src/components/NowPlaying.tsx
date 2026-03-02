import type { ReactNode } from 'react';
import { useEffect, useRef, useMemo, useState, useCallback } from 'react';
import { Music } from 'lucide-react';
import { api } from '../api/client';
import { useParty } from '../hooks/useParty';
import styles from './NowPlaying.module.css';

/**
 * Measures a text element for overflow and applies a scrolling marquee
 * animation via CSS custom properties when the text is truncated.
 */
function useMarquee(text: string | undefined) {
  const outerRef = useRef<HTMLDivElement>(null);
  const innerRef = useRef<HTMLSpanElement>(null);

  const measure = useCallback(() => {
    const outer = outerRef.current;
    const inner = innerRef.current;
    if (!outer || !inner) return;

    // Reset to measure natural width
    outer.classList.remove(styles.marquee);
    inner.style.removeProperty('--marquee-distance');
    inner.style.removeProperty('--marquee-duration');

    // Allow layout to settle
    requestAnimationFrame(() => {
      if (!outer || !inner) return;
      const overflow = inner.scrollWidth - outer.clientWidth;
      if (overflow > 1) {
        // ~27px/s scroll speed, min 3s
        const duration = Math.max(overflow / 27, 3);
        inner.style.setProperty('--marquee-distance', `-${overflow}px`);
        inner.style.setProperty('--marquee-duration', `${duration}s`);
        outer.classList.add(styles.marquee);
      }
    });
  }, []);

  useEffect(() => {
    measure();
  }, [text, measure]);

  // Re-measure on resize
  useEffect(() => {
    const observer = new ResizeObserver(measure);
    if (outerRef.current) observer.observe(outerRef.current);
    return () => observer.disconnect();
  }, [measure]);

  return { outerRef, innerRef };
}

/** Preload an image; resolves when ready, rejects on error. */
function preloadImage(url: string): Promise<void> {
  return new Promise((resolve, reject) => {
    const img = new Image();
    img.onload = () => resolve();
    img.onerror = reject;
    img.src = url;
  });
}

/**
 * Two-layer crossfade for the ambient album art background.
 * Returns the two layers (A/B) and which is currently visible,
 * swapping whenever a new URL finishes preloading.
 */
function useAmbientCrossfade(imageUrl: string | undefined) {
  const [layers, setLayers] = useState<{
    a: string | null; b: string | null; active: 'a' | 'b'; immediate: boolean;
  }>({
    a: null, b: null, active: 'a', immediate: false,
  });
  // Only updated inside async callbacks (rAF / preload .then) so that
  // React StrictMode's unmount+remount cycle doesn't stale-lock the ref.
  const appliedUrlRef = useRef<string | null>(null);
  const isFirstRef = useRef(true);

  // Clear the immediate flag after one frame so subsequent changes crossfade
  useEffect(() => {
    if (!layers.immediate) return;
    const id = requestAnimationFrame(() => {
      setLayers(prev => prev.immediate ? { ...prev, immediate: false } : prev);
    });
    return () => cancelAnimationFrame(id);
  }, [layers.immediate]);

  useEffect(() => {
    const url = imageUrl ?? null;
    if (url === appliedUrlRef.current) return;

    let cancelled = false;

    if (!url) {
      const id = requestAnimationFrame(() => {
        if (cancelled) return;
        appliedUrlRef.current = null;
        isFirstRef.current = true;
        setLayers({ a: null, b: null, active: 'a', immediate: false });
      });
      return () => { cancelled = true; cancelAnimationFrame(id); };
    }

    // Preload the image then update layers.
    // On first load this resolves near-instantly from browser cache
    // (the <img> tag already loaded it). Subsequent changes crossfade.
    preloadImage(url).then(() => {
      if (cancelled) return;
      appliedUrlRef.current = url;
      if (isFirstRef.current) {
        // First image: appear immediately (no 800ms fade-in from nothing)
        isFirstRef.current = false;
        setLayers({ a: url, b: null, active: 'a', immediate: true });
      } else {
        setLayers(prev => {
          const nextLayer = prev.active === 'a' ? 'b' : 'a';
          return { ...prev, [nextLayer]: url, active: nextLayer, immediate: false };
        });
      }
    }).catch(() => {
      // Image failed to load — ignore
    });

    return () => { cancelled = true; };
  }, [imageUrl]);

  return layers;
}

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
  const ambientLayers = useAmbientCrossfade(nowPlaying?.albumImageUrl);
  const trackMarquee = useMarquee(nowPlaying?.trackName);
  const artistMarquee = useMarquee(nowPlaying?.artistName);

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

  return (
    <div className={`${styles.container} ${!isHost ? styles.guestView : ''}`}>
      {ambientLayers.a && (
        <div
          className={`${styles.ambientLayer} ${ambientLayers.active === 'a' ? styles.ambientLayerVisible : ''}`}
          style={{
            backgroundImage: `url(${ambientLayers.a})`,
            ...(ambientLayers.immediate ? { transition: 'none' } : {}),
          }}
        />
      )}
      {ambientLayers.b && (
        <div
          className={`${styles.ambientLayer} ${ambientLayers.active === 'b' ? styles.ambientLayerVisible : ''}`}
          style={{
            backgroundImage: `url(${ambientLayers.b})`,
            ...(ambientLayers.immediate ? { transition: 'none' } : {}),
          }}
        />
      )}
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
          <div className={styles.track} ref={trackMarquee.outerRef}>
            <span ref={trackMarquee.innerRef}>{nowPlaying.trackName}</span>
          </div>
          <div className={styles.artist} ref={artistMarquee.outerRef}>
            <span ref={artistMarquee.innerRef}>{nowPlaying.artistName}</span>
          </div>
          {quip && <div className={styles.quip}>{quip}</div>}
        </div>
        {children}
      </div>
    </div>
  );
}
