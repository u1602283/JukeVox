import { useState } from 'react';
import { useParty } from '../context/PartyContext';
import { api } from '../api/client';
import type { SpotifyPlaylist } from '../types';

export function BasePlaylistSelector() {
  const { party, setParty, setQueue } = useParty();
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
      setQueue(result.queue);
      setParty({ ...party, basePlaylistId: result.basePlaylistId, basePlaylistName: result.basePlaylistName });
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
      setQueue(result.queue);
      setParty({ ...party, basePlaylistId: undefined, basePlaylistName: undefined });
    } catch (err) {
      console.error('Failed to clear base playlist:', err);
    }
  };

  if (showPicker) {
    return (
      <div className="base-playlist-selector">
        <div className="picker-header">
          <h4>Choose a base playlist</h4>
          <button className="picker-close" onClick={() => setShowPicker(false)}>&times;</button>
        </div>
        {loading ? (
          <p className="picker-loading">Loading playlists...</p>
        ) : (
          <div className="playlist-list">
            {playlists.map((pl) => (
              <button
                key={pl.id}
                className={`playlist-item ${pl.id === party.basePlaylistId ? 'playlist-item-active' : ''}`}
                onClick={() => handleSelect(pl.id)}
              >
                {pl.imageUrl && <img src={pl.imageUrl} alt="" className="playlist-thumb" />}
                <div className="playlist-info">
                  <span className="playlist-name">{pl.name}</span>
                  <span className="playlist-count">{pl.trackCount} tracks</span>
                </div>
              </button>
            ))}
          </div>
        )}
      </div>
    );
  }

  return (
    <div className="base-playlist-selector">
      {party.basePlaylistName ? (
        <div className="base-playlist-current">
          <span className="base-playlist-label">
            Base playlist: <strong>{party.basePlaylistName}</strong>
          </span>
          <button className="base-playlist-change" onClick={handleOpenPicker}>Change</button>
          <button className="base-playlist-clear" onClick={handleClear}>Clear</button>
        </div>
      ) : (
        <button className="base-playlist-set" onClick={handleOpenPicker}>
          Set Base Playlist
        </button>
      )}
    </div>
  );
}
