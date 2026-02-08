import { useState, useRef } from 'react';
import { Play, Pause, SkipBack, SkipForward, Volume2, Volume1, VolumeX } from 'lucide-react';
import { api } from '../api/client';
import { useParty } from '../hooks/useParty';
import styles from './HostControls.module.css';

export function HostControls() {
  const { nowPlaying, party } = useParty();
  const [volume, setVolume] = useState(nowPlaying?.volumePercent ?? 50);
  const volumeTimer = useRef<ReturnType<typeof setTimeout> | null>(null);

  if (!party?.isHost || !party.spotifyConnected) return null;

  const handlePause = () => api.pause().catch(() => {});
  const handleResume = () => api.resume().catch(() => {});
  const handlePrevious = () => api.previous(nowPlaying?.progressMs ?? 0).catch(() => {});
  const handleSkip = () => api.skip().catch(() => {});

  const handleVolumeChange = (e: React.ChangeEvent<HTMLInputElement>) => {
    const val = parseInt(e.target.value, 10);
    setVolume(val);
    if (volumeTimer.current) clearTimeout(volumeTimer.current);
    volumeTimer.current = setTimeout(() => {
      api.setVolume(val).catch(() => {});
    }, 300);
  };

  const supportsVolume = nowPlaying?.supportsVolume ?? true;
  const VolumeIcon = volume === 0 ? VolumeX : volume < 50 ? Volume1 : Volume2;

  return (
    <div className={styles.container}>
      <div className={styles.buttons}>
        <button onClick={handlePrevious} className={styles.controlBtn} title="Previous">
          <SkipBack size={22} />
        </button>
        {nowPlaying?.isPlaying ? (
          <button onClick={handlePause} className={styles.mainBtn} title="Pause">
            <Pause size={26} fill="currentColor" />
          </button>
        ) : (
          <button onClick={handleResume} className={styles.mainBtn} title="Play">
            <Play size={26} fill="currentColor" style={{ marginLeft: 2 }} />
          </button>
        )}
        <button onClick={handleSkip} className={styles.controlBtn} title="Next">
          <SkipForward size={22} />
        </button>
      </div>
      {supportsVolume && (
        <div className={styles.volume}>
          <span className={styles.volumeIcon}>
            <VolumeIcon size={18} />
          </span>
          <input
            type="range"
            min={0}
            max={100}
            value={volume}
            onChange={handleVolumeChange}
            className={styles.volumeSlider}
          />
          <span className={styles.volumeValue}>{volume}%</span>
        </div>
      )}
    </div>
  );
}
