import { useState, useEffect, useRef } from 'react';
import { Search, HelpCircle } from 'lucide-react';
import { useParty } from '../hooks/useParty';
import { useSearch } from '../hooks/useSearch';
import { NowPlaying } from '../components/NowPlaying';
import { QueueList } from '../components/QueueList';
import { SearchOverlay } from '../components/SearchOverlay';
import { HelpOverlay } from '../components/HelpOverlay';
import { CreditsBadge } from '../components/CreditsBadge';
import { TabIndicator } from '../components/TabIndicator';
import styles from './PartyPage.module.css';

export function PartyPage() {
  const { party } = useParty();
  const { query, setQuery, results, loading } = useSearch();
  const [scrolled, setScrolled] = useState(false);
  const sentinelRef = useRef<HTMLDivElement>(null);
  const [searchOpen, setSearchOpen] = useState(false);
  const [mobileView, setMobileView] = useState<'playing' | 'queue'>('playing');
  const [helpOpen, setHelpOpen] = useState(false);

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

  const tabIndex = mobileView === 'queue' ? 1 : 0;

  const handleCloseSearch = () => {
    setSearchOpen(false);
    setQuery('');
  };

  if (!party) return null;

  return (
    <div className={styles.page}>
      <div ref={sentinelRef} style={{ height: 1 }} />
      <header className={`${styles.header} ${scrolled ? styles.headerScrolled : ''}`}>
        <h1 className={styles.headerTitle}>
          {party.displayName ? `Hi, ${party.displayName}!` : 'JukeVox'}
        </h1>
        <div className={styles.headerRight}>
          <CreditsBadge />
          <span className={styles.inviteCode}>
            <span className={styles.inviteCodeValue}>{party.inviteCode}</span>
          </span>
          {party.isHost && party.spotifyConnected && (
            <span className={styles.spotifyStatus}>Connected</span>
          )}
          <div className={styles.headerIcons}>
            {!party.isHost && (
              <button
                className={styles.searchToggle}
                onClick={() => setHelpOpen(true)}
                aria-label="How it works"
              >
                <HelpCircle size={22} />
              </button>
            )}
            <button
              className={styles.searchToggle}
              onClick={() => setSearchOpen(true)}
              aria-label="Search for a song"
            >
              <Search size={22} />
            </button>
          </div>
        </div>
      </header>

      <div className={`${styles.contentGrid} ${styles.hasSlideTrack}`}>
        <div className={styles.slideTrack} style={{ '--tab-index': tabIndex } as React.CSSProperties}>
          <div className={`${styles.slidePanel} ${styles.slidePanelFirst}`}>
            <div className={styles.heroColumn}>
              <NowPlaying />
            </div>
          </div>
          <div className={styles.slidePanel}>
            <QueueList />
          </div>
        </div>
      </div>

      <HelpOverlay open={helpOpen} onClose={() => setHelpOpen(false)} />

      <SearchOverlay
        open={searchOpen}
        onClose={handleCloseSearch}
        query={query}
        onQueryChange={setQuery}
        results={results}
        loading={loading}
      />

      <nav className={styles.mobileNav}>
        <TabIndicator tabIndex={tabIndex} tabCount={2} />
        <button
          className={`${styles.mobileNavBtn} ${mobileView === 'playing' ? styles.mobileNavBtnActive : ''}`}
          onClick={() => setMobileView('playing')}
        >
          Now Playing
        </button>
        <button
          className={`${styles.mobileNavBtn} ${mobileView === 'queue' ? styles.mobileNavBtnActive : ''}`}
          onClick={() => setMobileView('queue')}
        >
          Queue
        </button>
      </nav>
    </div>
  );
}
