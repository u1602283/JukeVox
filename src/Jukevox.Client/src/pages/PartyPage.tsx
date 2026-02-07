import { useParty } from '../context/PartyContext';
import { useSearch } from '../hooks/useSearch';
import { NowPlaying } from '../components/NowPlaying';
import { QueueList } from '../components/QueueList';
import { SearchBar } from '../components/SearchBar';
import { SearchResults } from '../components/SearchResults';
import { CreditsBadge } from '../components/CreditsBadge';

export function PartyPage() {
  const { party } = useParty();
  const { query, setQuery, results, loading } = useSearch();

  if (!party) return null;

  return (
    <div className="party-page">
      <header className="party-header">
        <h1>JukeVox</h1>
        <div className="party-info">
          <span className="invite-code">Code: <strong>{party.inviteCode}</strong></span>
          {party.spotifyConnected && (
            <span className="spotify-status connected">Spotify Connected</span>
          )}
        </div>
      </header>

      <CreditsBadge />

      <NowPlaying />

      <div className="search-section">
        <SearchBar query={query} onQueryChange={setQuery} loading={loading} />
        <SearchResults results={results} />
      </div>

      <QueueList />
    </div>
  );
}
