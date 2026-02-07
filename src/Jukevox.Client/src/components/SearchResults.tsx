import { useState } from 'react';
import { api } from '../api/client';
import { useParty } from '../context/PartyContext';
import type { SearchResult } from '../types';

function formatDuration(ms: number): string {
  const mins = Math.floor(ms / 60000);
  const secs = Math.floor((ms % 60000) / 1000);
  return `${mins}:${secs.toString().padStart(2, '0')}`;
}

interface SearchResultsProps {
  results: SearchResult[];
}

export function SearchResults({ results }: SearchResultsProps) {
  const { party, setQueue, setCredits, credits } = useParty();
  const [addingUri, setAddingUri] = useState<string | null>(null);

  if (results.length === 0) return null;

  const canAdd = party?.isHost || (credits !== null && credits > 0);

  const handleAdd = async (track: SearchResult) => {
    setAddingUri(track.trackUri);
    try {
      const result = await api.addToQueue({
        trackUri: track.trackUri,
        trackName: track.trackName,
        artistName: track.artistName,
        albumName: track.albumName,
        albumImageUrl: track.albumImageUrl,
        durationMs: track.durationMs,
      });
      setQueue(result.queue);
      if (result.creditsRemaining !== undefined) {
        setCredits(result.creditsRemaining);
      }
    } catch (err: any) {
      console.error('Failed to add track:', err);
    } finally {
      setAddingUri(null);
    }
  };

  return (
    <div className="search-results">
      {results.map((track) => (
        <div key={track.trackUri} className="search-result-item">
          {track.albumImageUrl && (
            <img src={track.albumImageUrl} alt="" className="track-thumb" />
          )}
          <div className="track-info">
            <span className="track-name">{track.trackName}</span>
            <span className="track-artist">{track.artistName}</span>
            <span className="track-meta">
              {track.albumName} &middot; {formatDuration(track.durationMs)}
            </span>
          </div>
          <button
            className="add-btn"
            onClick={() => handleAdd(track)}
            disabled={!canAdd || addingUri === track.trackUri}
          >
            {addingUri === track.trackUri ? '...' : '+'}
          </button>
        </div>
      ))}
    </div>
  );
}
