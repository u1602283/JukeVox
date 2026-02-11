import { useRef, useEffect } from 'react';
import styles from '../pages/PartyPage.module.css';

const PINCH_DEPTH = 0.30;
const PINCH_SIGMA = 0.12;
const EDGE_MARGIN = 0.08;
const NUM_POINTS = 20;
const SMOOTHING = 0.4;   // frame-to-frame carry-over for soft inertia
const SETTLE_MS = 500;

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

    const el = ref.current;
    const parent = el?.parentElement;
    if (!el || !parent) return;

    let running = true;
    let rafId: number;
    const prev = new Float64Array(NUM_POINTS + 1);

    const animate = () => {
      if (!running) return;

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
