import { useRef, useEffect } from 'react';
import styles from '../pages/PartyPage.module.css';

const PINCH_DEPTH = 0.30;
const PINCH_SIGMA = 0.12;
const EDGE_MARGIN = 0.08;
const NUM_POINTS = 20;
const SMOOTHING = 0.4;   // frame-to-frame carry-over for soft inertia
const SETTLE_MS = 800;
const JANK_FRAME_MS = 32;    // a frame slower than ~30fps counts as a dropped frame
const JANK_TRIP_COUNT = 5;   // sustained dropped frames before we give up

// The liquid-pinch clip-path is a per-frame flourish (recomputes a polygon and
// reads layout every frame). Rather than guess device capability up front
// (navigator.hardwareConcurrency is unreliable — iOS Safari under-reports it),
// we run it and watch real frame timing: if a device can't sustain it, we bail
// and disable it for the rest of the session. The CSS transform still slides
// the pill smoothly regardless.
let pinchDisabled = false;

function pinchAllowed(): boolean {
  if (typeof window === 'undefined') return false;
  if (pinchDisabled) return false;
  return !window.matchMedia('(prefers-reduced-motion: reduce)').matches;
}

interface TabIndicatorProps {
  tabIndex: number;
  tabCount: number;
}

export function TabIndicator({ tabIndex, tabCount }: TabIndicatorProps) {
  const ref = useRef<HTMLDivElement>(null);
  const isFirst = useRef(true);

  useEffect(() => {
    if (isFirst.current) {
      isFirst.current = false;
      return;
    }

    // The pill itself slides via CSS transform; only the optional pinch
    // flourish runs here, and only where the device can sustain it.
    if (!pinchAllowed()) return;

    const el = ref.current;
    const parent = el?.parentElement;
    if (!el || !parent) return;

    let running = true;
    let rafId: number;
    let lastTs: number | null = null;
    let slowFrames = 0;
    const prev = new Float64Array(NUM_POINTS + 1);

    const animate = (ts: number) => {
      if (!running) return;

      // Watch real frame timing; if this device can't keep up, disable the
      // pinch for the session and fall back to the plain transform slide.
      if (lastTs !== null) {
        if (ts - lastTs > JANK_FRAME_MS) {
          if (++slowFrames >= JANK_TRIP_COUNT) {
            pinchDisabled = true;
            running = false;
            el.style.clipPath = '';
            return;
          }
        } else if (slowFrames > 0) {
          slowFrames--;
        }
      }
      lastTs = ts;

      const pRect = parent.getBoundingClientRect();
      const eRect = el.getBoundingClientRect();
      const navW = pRect.width;
      const pillL = eRect.left - pRect.left;
      const pillW = eRect.width;

      if (pillW < 1) {
        rafId = requestAnimationFrame(animate);
        return;
      }

      // Tab boundary positions in nav-relative coordinates
      const stride = (navW - 16) / tabCount;
      const boundaries: number[] = [];
      for (let j = 1; j < tabCount; j++) {
        boundaries.push(8 + j * stride);
      }

      let maxD = 0;
      const top: string[] = [];
      const bot: string[] = [];

      for (let i = 0; i <= NUM_POINTS; i++) {
        const x = i / NUM_POINTS;
        let ideal = 0;
        for (const b of boundaries) {
          const bx = (b - pillL) / pillW;
          const edgeDist = Math.min(Math.max(bx, 0), 1);
          const scale = Math.min(1, Math.min(edgeDist, 1 - edgeDist) / EDGE_MARGIN);
          if (scale > 0) {
            const diff = (x - bx) / PINCH_SIGMA;
            ideal += Math.exp(-0.5 * diff * diff) * scale;
          }
        }
        ideal = Math.min(ideal, 1) * PINCH_DEPTH;

        // Blend with previous frame for soft material inertia
        const d = ideal * (1 - SMOOTHING) + prev[i] * SMOOTHING;
        prev[i] = d;

        if (d > maxD) maxD = d;
        const px = (x * 100).toFixed(1);
        top.push(`${px}% ${(d * 100).toFixed(1)}%`);
        bot.unshift(`${px}% ${(100 - d * 100).toFixed(1)}%`);
      }

      if (maxD > 0.005) {
        el.style.clipPath = `polygon(${top.join(', ')}, ${bot.join(', ')})`;
      } else {
        el.style.clipPath = '';
      }

      rafId = requestAnimationFrame(animate);
    };

    rafId = requestAnimationFrame(animate);

    const timer = setTimeout(() => {
      running = false;
      cancelAnimationFrame(rafId);
      if (el) el.style.clipPath = '';
    }, SETTLE_MS);

    return () => {
      running = false;
      cancelAnimationFrame(rafId);
      clearTimeout(timer);
      if (el) el.style.clipPath = '';
    };
  }, [tabIndex, tabCount]);

  return (
    <div
      ref={ref}
      className={styles.indicator}
      style={{
        '--tab-index': tabIndex,
        '--tab-count': tabCount,
      } as React.CSSProperties}
    />
  );
}
