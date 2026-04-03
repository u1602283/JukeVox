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

  return (
    <div className={styles.page}>
      <div ref={sentinelRef} style={{ height: 1 }} />
      <header className={`${styles.header} ${scrolled ? styles.headerScrolled : ''}`}>
        {headerTitle}
        {headerRight}
      </header>

      <div className={`${styles.contentGrid} ${styles.hasSlideTrack}`}>
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
