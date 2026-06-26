import { useState, useEffect, useRef, type ReactNode } from 'react';
import { TabIndicator } from './TabIndicator';
import styles from '../pages/PartyPage.module.css';

export interface PanelDefinition {
  label: string;
  content: ReactNode | ((active: boolean) => ReactNode);
  first?: boolean;
  desktopHidden?: boolean;
}

interface PartyLayoutProps {
  headerTitle: ReactNode;
  headerRight: ReactNode;
  panels: PanelDefinition[];
  overlays?: ReactNode;
}

export function PartyLayout({ headerTitle, headerRight, panels, overlays }: PartyLayoutProps) {
  const [scrolled, setScrolled] = useState(false);
  const sentinelRef = useRef<HTMLDivElement>(null);
  const [tabIndex, setTabIndex] = useState(0);

  useEffect(() => {
    const sentinel = sentinelRef.current;
    if (!sentinel) return;
    const observer = new IntersectionObserver(
      ([entry]) => setScrolled(!entry.isIntersecting),
      { threshold: 1 }
    );
    observer.observe(sentinel);
    return () => observer.disconnect();
  }, []);

  // Horizontal swipe to change tabs on mobile. We reuse the existing slide
  // transition by just updating tabIndex; the gesture only commits when it's
  // clearly horizontal (so vertical scrolling is unaffected) and didn't start
  // on a horizontal control like the seek/volume slider.
  const swipe = useRef({ x: 0, y: 0, tracking: false, axis: null as null | 'x' | 'y' });

  const onTouchStart = (e: React.TouchEvent) => {
    if (e.touches.length !== 1) {
      swipe.current.tracking = false;
      return;
    }
    const target = e.target as HTMLElement;
    if (target.closest('input[type="range"], [data-no-swipe]')) {
      swipe.current.tracking = false;
      return;
    }
    const t = e.touches[0];
    swipe.current = { x: t.clientX, y: t.clientY, tracking: true, axis: null };
  };

  const onTouchMove = (e: React.TouchEvent) => {
    const s = swipe.current;
    if (!s.tracking || s.axis !== null) return;
    const t = e.touches[0];
    const dx = Math.abs(t.clientX - s.x);
    const dy = Math.abs(t.clientY - s.y);
    if (dx > 8 || dy > 8) s.axis = dx > dy ? 'x' : 'y';
  };

  const onTouchEnd = (e: React.TouchEvent) => {
    const s = swipe.current;
    if (!s.tracking) return;
    s.tracking = false;
    if (s.axis !== 'x') return;
    const dx = e.changedTouches[0].clientX - s.x;
    const THRESHOLD = 55;
    if (dx <= -THRESHOLD) setTabIndex((i) => Math.min(panels.length - 1, i + 1));
    else if (dx >= THRESHOLD) setTabIndex((i) => Math.max(0, i - 1));
  };

  return (
    <div className={styles.page}>
      <div ref={sentinelRef} style={{ height: 1 }} />
      <header className={`${styles.header} ${scrolled ? styles.headerScrolled : ''}`}>
        {headerTitle}
        {headerRight}
      </header>

      <div
        className={`${styles.contentGrid} ${styles.hasSlideTrack}`}
        onTouchStart={onTouchStart}
        onTouchMove={onTouchMove}
        onTouchEnd={onTouchEnd}
      >
        <div className={styles.slideTrack} style={{ '--tab-index': tabIndex } as React.CSSProperties}>
          {panels.map((panel, i) => (
            <div
              key={i}
              className={`${styles.slidePanel}${panel.first ? ` ${styles.slidePanelFirst}` : ''}${panel.desktopHidden ? ` ${styles.desktopHidden}` : ''}`}
            >
              {panel.first ? (
                <div className={styles.heroColumn}>
                  {typeof panel.content === 'function' ? panel.content(tabIndex === i) : panel.content}
                </div>
              ) : (
                typeof panel.content === 'function' ? panel.content(tabIndex === i) : panel.content
              )}
            </div>
          ))}
        </div>
      </div>

      {overlays}

      <nav className={styles.mobileNav}>
        <TabIndicator tabIndex={tabIndex} tabCount={panels.length} />
        {panels.map((panel, i) => (
          <button
            key={i}
            className={`${styles.mobileNavBtn} ${tabIndex === i ? styles.mobileNavBtnActive : ''}`}
            onClick={() => setTabIndex(i)}
          >
            {panel.label}
          </button>
        ))}
      </nav>
    </div>
  );
}
