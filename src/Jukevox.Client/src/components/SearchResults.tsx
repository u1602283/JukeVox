import { useState } from 'react';
import { Plus, Check, Loader2 } from 'lucide-react';
import { api } from '../api/client';
import { useParty } from '../hooks/useParty';
import type { SearchResult } from '../types';
import styles from './SearchResults.module.css';

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
  const [addedUri, setAddedUri] = useState<string | null>(null);

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
      // Brief checkmark
      setAddedUri(track.trackUri);
      setTimeout(() => setAddedUri(null), 1500);
    } catch (err: unknown) {
      console.error('Failed to add track:', err);
    } finally {
      setAddingUri(null);
    }
  };

  return (
    <div className={styles.container}>
      {results.map((track, index) => (
        <div
          key={track.trackUri}
          className={styles.item}
          style={{ animationDelay: `${index * 50}ms` }}
        >
          {track.albumImageUrl && (
            <img src={track.albumImageUrl} alt="" className={styles.thumb} />
          )}
          <div className={styles.trackInfo}>
            <span className={styles.trackName}>{track.trackName}</span>
            <span className={styles.trackArtist}>{track.artistName}</span>
            <span className={styles.trackMeta}>
              {track.albumName} &middot; {formatDuration(track.durationMs)}
            </span>
          </div>
          <button
            className={styles.addBtn}
            onClick={() => handleAdd(track)}
            disabled={!canAdd || addingUri === track.trackUri || addedUri === track.trackUri}
          >
            {addingUri === track.trackUri ? (
              <Loader2 size={18} className={styles.addedCheck} />
            ) : addedUri === track.trackUri ? (
              <Check size={18} className={styles.addedCheck} />
            ) : (
              <Plus size={18} />
            )}
          </button>
        </div>
      ))}
    </div>
  );
}
