import { useState } from 'react';
import { Search, HelpCircle, Share2 } from 'lucide-react';
import { useParty } from '../hooks/useParty';
import { useSearch } from '../hooks/useSearch';
import { NowPlaying } from '../components/NowPlaying';
import { QueueList } from '../components/QueueList';
import { SearchOverlay } from '../components/SearchOverlay';
import { HelpOverlay } from '../components/HelpOverlay';
import { ShareOverlay } from '../components/ShareOverlay';
import { CreditsBadge } from '../components/CreditsBadge';
import { PartyLayout } from '../components/PartyLayout';
import type { PanelDefinition } from '../components/PartyLayout';
import styles from './PartyPage.module.css';

export function PartyPage() {
  const { party } = useParty();
  const { query, setQuery, results, loading } = useSearch();
  const [searchOpen, setSearchOpen] = useState(false);
  const [helpOpen, setHelpOpen] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);

  const handleCloseSearch = () => {
    setSearchOpen(false);
    setQuery('');
  };

  if (!party) return null;

  const panels: PanelDefinition[] = [
    { label: 'Now Playing', first: true, content: <NowPlaying /> },
    { label: 'Queue', content: <QueueList /> },
  ];

  return (
    <PartyLayout
      headerTitle={
        <h1 className={styles.headerTitle}>
          {party.displayName ? `Hi, ${party.displayName}!` : 'JukeVox'}
        </h1>
      }
      headerRight={
        <div className={styles.headerRight}>
          <CreditsBadge />
          {party.isHost && party.spotifyConnected && (
            <span className={styles.spotifyStatus}>Connected</span>
          )}
          <div className={styles.headerIcons}>
            <button className={styles.inviteCode} onClick={() => setShareOpen(true)} aria-label="Share party code">
              <span className={styles.inviteCodeValue}>{party.inviteCode} <Share2 size={13} /></span>
              <span className={styles.inviteCodeCompact}><Share2 size={18} /></span>
            </button>
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
      }
      panels={panels}
      overlays={
        <>
          <HelpOverlay open={helpOpen} onClose={() => setHelpOpen(false)} />
          <ShareOverlay open={shareOpen} onClose={() => setShareOpen(false)} inviteCode={party.inviteCode} />
          <SearchOverlay
            open={searchOpen}
            onClose={handleCloseSearch}
            query={query}
            onQueryChange={setQuery}
            results={results}
            loading={loading}
          />
        </>
      }
    />
  );
}
