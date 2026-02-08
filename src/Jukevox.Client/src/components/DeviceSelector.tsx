import { useState, useEffect, useRef } from 'react';
import { Monitor, Smartphone, Speaker, Tv, Music, X, Loader2 } from 'lucide-react';
import { api } from '../api/client';
import { useParty } from '../hooks/useParty';
import type { SpotifyDevice } from '../types';
import styles from './DeviceSelector.module.css';

function DeviceIcon({ type }: { type: string }) {
  const t = type.toLowerCase();
  if (t === 'computer') return <Monitor size={20} />;
  if (t === 'smartphone') return <Smartphone size={20} />;
  if (t === 'speaker') return <Speaker size={20} />;
  if (t === 'tv') return <Tv size={20} />;
  return <Music size={20} />;
}

export function DeviceSelector() {
  const { party, nowPlaying } = useParty();
  const [devices, setDevices] = useState<SpotifyDevice[]>([]);
  const [loading, setLoading] = useState(false);
  const [selecting, setSelecting] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [error, setError] = useState('');
  const panelRef = useRef<HTMLDivElement>(null);

  // Close on click outside — must be before early return to satisfy rules-of-hooks
  useEffect(() => {
    if (!open) return;
    const handleClick = (e: MouseEvent) => {
      if (panelRef.current && !panelRef.current.contains(e.target as Node)) {
        setOpen(false);
      }
    };
    document.addEventListener('pointerdown', handleClick);
    return () => document.removeEventListener('pointerdown', handleClick);
  }, [open]);

  if (!party?.isHost || !party.spotifyConnected) return null;

  const loadDevices = async () => {
    setLoading(true);
    setError('');
    try {
      const d = await api.getDevices();
      setDevices(d);
    } catch {
      setError('Failed to load devices');
    } finally {
      setLoading(false);
    }
  };

  const handleSelect = async (e: React.MouseEvent, deviceId: string) => {
    e.stopPropagation();
    setSelecting(deviceId);
    setError('');
    try {
      await api.selectDevice(deviceId);
      setOpen(false);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to select device');
    } finally {
      setSelecting(null);
    }
  };

  const toggleOpen = () => {
    const next = !open;
    setOpen(next);
    if (next) loadDevices();
  };

  const activeDevice = nowPlaying?.deviceName;

  return (
    <div className={styles.container} ref={panelRef}>
      <button
        onClick={toggleOpen}
        className={`${styles.trigger} ${open ? styles.triggerOpen : ''}`}
      >
        <Speaker size={18} />
        {activeDevice && <span className={styles.triggerLabel}>{activeDevice}</span>}
      </button>

      {open && (
        <div className={styles.panel}>
          <div className={styles.panelHeader}>
            <span>Connect to a device</span>
            <button className={styles.closeBtn} onClick={() => setOpen(false)}>
              <X size={18} />
            </button>
          </div>

          <div className={styles.panelBody}>
            {loading ? (
              <div className={styles.panelEmpty}>Searching for devices...</div>
            ) : devices.length === 0 ? (
              <div className={styles.panelEmpty}>
                No devices found.<br />Open Spotify on a device.
              </div>
            ) : (
              devices.map((d) => (
                <button
                  key={d.id}
                  className={`${styles.item} ${d.isActive ? styles.itemActive : ''}`}
                  onClick={(e) => handleSelect(e, d.id)}
                  disabled={selecting !== null}
                >
                  <span className={`${styles.itemIcon} ${d.isActive ? styles.itemIconActive : ''}`}>
                    <DeviceIcon type={d.type} />
                  </span>
                  <span className={styles.itemInfo}>
                    <span className={styles.itemName}>{d.name}</span>
                    {d.isActive && <span className={styles.itemStatus}>Currently playing</span>}
                  </span>
                  {selecting === d.id && (
                    <Loader2 size={16} className={styles.itemSpinner} />
                  )}
                </button>
              ))
            )}
          </div>

          {error && <div className={styles.error}>{error}</div>}
        </div>
      )}
    </div>
  );
}
