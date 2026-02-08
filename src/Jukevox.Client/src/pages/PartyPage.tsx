import { useState, useEffect, useRef } from 'react';
import { Search } from 'lucide-react';
import { useParty } from '../hooks/useParty';
import { useSearch } from '../hooks/useSearch';
import { NowPlaying } from '../components/NowPlaying';
import { QueueList } from '../components/QueueList';
import { SearchOverlay } from '../components/SearchOverlay';
import { CreditsBadge } from '../components/CreditsBadge';
import styles from './PartyPage.module.css';

export function PartyPage() {
  const { party } = useParty();
  const { query, setQuery, results, loading } = useSearch();
  const [scrolled, setScrolled] = useState(false);
  const sentinelRef = useRef<HTMLDivElement>(null);
  const [searchOpen, setSearchOpen] = useState(false);
  const [mobileView, setMobileView] = useState<'playing' | 'queue'>('playing');

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

  const handleCloseSearch = () => {
    setSearchOpen(false);
    setQuery('');
  };

  if (!party) return null;

  return (
    <div className={styles.page}>
      <div ref={sentinelRef} style={{ height: 1 }} />
      <header className={`${styles.header} ${scrolled ? styles.headerScrolled : ''}`}>
        <h1 className={styles.headerTitle}>JukeVox</h1>
        <div className={styles.headerRight}>
          <CreditsBadge />
          <span className={styles.inviteCode}>
            <span className={styles.inviteCodeValue}>{party.inviteCode}</span>
          </span>
          {party.spotifyConnected && (
            <span className={styles.spotifyStatus}>Connected</span>
          )}
          <button
            className={styles.searchToggle}
            onClick={() => setSearchOpen(true)}
            aria-label="Search for a song"
          >
            <Search size={20} />
          </button>
        </div>
      </header>

      <div className={styles.contentGrid}>
        <div className={`${styles.heroColumn} ${mobileView !== 'playing' ? styles.mobileHidden : ''}`}>
          <NowPlaying />
        </div>
        <div className={mobileView !== 'queue' ? styles.mobileHidden : ''}>
          <QueueList />
        </div>
      </div>

      <SearchOverlay
        open={searchOpen}
        onClose={handleCloseSearch}
        query={query}
        onQueryChange={setQuery}
        results={results}
        loading={loading}
      />

      <nav className={styles.mobileNav}>
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
