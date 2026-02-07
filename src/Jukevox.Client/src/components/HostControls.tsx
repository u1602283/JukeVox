import { useState, useRef } from 'react';
import { api } from '../api/client';
import { useParty } from '../context/PartyContext';

export function HostControls() {
  const { nowPlaying, party } = useParty();
  const [volume, setVolume] = useState(nowPlaying?.volumePercent ?? 50);
  const volumeTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  if (!party?.isHost || !party.spotifyConnected) return null;

  const handlePause = () => api.pause().catch(() => {});
  const handleResume = () => api.resume().catch(() => {});
  const handlePrevious = () => api.previous().catch(() => {});
  const handleSkip = () => api.skip().catch(() => {});

  const handleVolumeChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const val = parseInt(e.target.value, 10);
    setVolume(val);
    if (volumeTimer.current) clearTimeout(volumeTimer.current);
    volumeTimer.current = setTimeout(() => {
      api.setVolume(val).catch(() => {});
    }, 300);
  };

  return (
    <div className="host-controls">
      <div className="playback-buttons">
        <button onClick={handlePrevious} className="control-btn" title="Previous">
          <svg viewBox="0 0 24 24" fill="currentColor" width="22" height="22">
            <path d="M6 6h2v12H6zm3.5 6 8.5 6V6z" />
          </svg>
        </button>
        {nowPlaying?.isPlaying ? (
          <button onClick={handlePause} className="control-btn control-btn-main" title="Pause">
            <svg viewBox="0 0 24 24" fill="currentColor" width="28" height="28">
              <path d="M6 19h4V5H6zm8-14v14h4V5z" />
            </svg>
          </button>
        ) : (
          <button onClick={handleResume} className="control-btn control-btn-main" title="Play">
            <svg viewBox="0 0 24 24" fill="currentColor" width="28" height="28">
              <path d="M8 5v14l11-7z" />
            </svg>
          </button>
        )}
        <button onClick={handleSkip} className="control-btn" title="Next">
          <svg viewBox="0 0 24 24" fill="currentColor" width="22" height="22">
            <path d="M6 18l8.5-6L6 6v12zm8.5 0h2V6h-2v12z" transform="translate(1,0)" />
          </svg>
        </button>
      </div>
      <div className="volume-control">
        <svg viewBox="0 0 24 24" fill="currentColor" width="18" height="18" className="volume-icon">
          {volume === 0 ? (
            <path d="M16.5 12c0-1.77-1.02-3.29-2.5-4.03v2.21l2.45 2.45c.03-.2.05-.41.05-.63zm2.5 0c0 .94-.2 1.82-.54 2.64l1.51 1.51A8.796 8.796 0 0021 12c0-4.28-2.99-7.86-7-8.77v2.06c2.89.86 5 3.54 5 6.71zM4.27 3L3 4.27 7.73 9H3v6h4l5 5v-6.73l4.25 4.25c-.67.52-1.42.93-2.25 1.18v2.06a8.99 8.99 0 003.69-1.81L19.73 21 21 19.73l-9-9L4.27 3zM12 4L9.91 6.09 12 8.18V4z" />
          ) : volume < 50 ? (
            <path d="M18.5 12c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM5 9v6h4l5 5V4L9 9H5z" />
          ) : (
            <path d="M3 9v6h4l5 5V4L7 9H3zm13.5 3c0-1.77-1.02-3.29-2.5-4.03v8.05c1.48-.73 2.5-2.25 2.5-4.02zM14 3.23v2.06c2.89.86 5 3.54 5 6.71s-2.11 5.85-5 6.71v2.06c4.01-.91 7-4.49 7-8.77s-2.99-7.86-7-8.77z" />
          )}
        </svg>
        <input
          type="range"
          min={0}
          max={100}
          value={volume}
          onChange={handleVolumeChange}
        />
        <span className="volume-value">{volume}%</span>
      </div>
    </div>
  );
}
