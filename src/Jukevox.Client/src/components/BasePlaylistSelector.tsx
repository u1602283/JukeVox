import { useState } from 'react';
import { X } from 'lucide-react';
import { useParty } from '../hooks/useParty';
import { api } from '../api/client';
import type { SpotifyPlaylist } from '../types';
import styles from './BasePlaylistSelector.module.css';

export function BasePlaylistSelector() {
  const { party, setParty } = useParty();
  const [playlists, setPlaylists] = useState<SpotifyPlaylist[]>([]);
  const [showPicker, setShowPicker] = useState(false);
  const [loading, setLoading] = useState(false);

  if (!party?.isHost || !party.spotifyConnected) return null;

  const handleOpenPicker = async () => {
    setShowPicker(true);
    setLoading(true);
    try {
      const result = await api.getPlaylists();
      setPlaylists(result);
    } catch (err) {
      console.error('Failed to fetch playlists:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleSelect = async (playlistId: string) => {
    setLoading(true);
    try {
      const result = await api.setBasePlaylist(playlistId);
      setParty({ ...party, basePlaylistId: result.basePlaylistId, basePlaylistName: result.basePlaylistName, queue: result.queue });
      setShowPicker(false);
    } catch (err) {
      console.error('Failed to set base playlist:', err);
    } finally {
      setLoading(false);
    }
  };

  const handleClear = async () => {
    try {
      const result = await api.clearBasePlaylist();
      setParty({ ...party, basePlaylistId: undefined, basePlaylistName: undefined, queue: result.queue });
    } catch (err) {
      console.error('Failed to clear base playlist:', err);
    }
  };

  if (showPicker) {
    return (
      <div className={styles.container}>
        <div className={styles.pickerHeader}>
          <h4 className={styles.pickerTitle}>Choose a base playlist</h4>
          <button className={styles.pickerClose} onClick={() => setShowPicker(false)}>
            <X size={18} />
          </button>
        </div>
        {loading ? (
          <p className={styles.pickerLoading}>Loading playlists...</p>
        ) : (
          <div className={styles.list}>
            {playlists.map((pl) => (
              <button
                key={pl.id}
                className={`${styles.playlistItem} ${pl.id === party.basePlaylistId ? styles.playlistItemActive : ''}`}
                onClick={() => handleSelect(pl.id)}
              >
                {pl.imageUrl && <img src={pl.imageUrl} alt="" className={styles.playlistThumb} />}
                <div className={styles.playlistInfo}>
                  <span className={styles.playlistName}>{pl.name}</span>
                  <span className={styles.playlistCount}>{pl.trackCount} tracks</span>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>
    );
  }

  return (
    <div className={styles.container}>
      {party.basePlaylistName ? (
        <div className={styles.current}>
          <span className={styles.label}>
            Base playlist: <strong>{party.basePlaylistName}</strong>
          </span>
          <button className={styles.changeBtn} onClick={handleOpenPicker}>Change</button>
          <button className={styles.clearBtn} onClick={handleClear}>Clear</button>
        </div>
      ) : (
        <button className={styles.setBtn} onClick={handleOpenPicker}>
          Set Base Playlist
        </button>
      )}
    </div>
  );
}
