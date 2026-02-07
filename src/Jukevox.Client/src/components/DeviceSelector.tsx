import { useState, useEffect, useRef } from 'react';
import { api } from '../api/client';
import { useParty } from '../context/PartyContext';
import type { SpotifyDevice } from '../types';

function DeviceIcon({ type }: { type: string }) {
  const t = type.toLowerCase();
  if (t === 'computer')
    return (
      <svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20">
        <path d="M20 18c1.1 0 2-.9 2-2V6c0-1.1-.9-2-2-2H4c-1.1 0-2 .9-2 2v10c0 1.1.9 2 2 2H0v2h24v-2h-4zM4 6h16v10H4V6z" />
      </svg>
    );
  if (t === 'smartphone')
    return (
      <svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20">
        <path d="M16 1H8C6.34 1 5 2.34 5 4v16c0 1.66 1.34 3 3 3h8c1.66 0 3-1.34 3-3V4c0-1.66-1.34-3-3-3zm-2 20h-4v-1h4v1zm3.25-3H6.75V4h10.5v14z" />
      </svg>
    );
  if (t === 'speaker')
    return (
      <svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20">
        <path d="M17 2H7c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-5 2c1.1 0 2 .9 2 2s-.9 2-2 2-2-.9-2-2 .9-2 2-2zm0 16c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z" />
      </svg>
    );
  if (t === 'tv')
    return (
      <svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20">
        <path d="M21 3H3c-1.1 0-2 .9-2 2v12c0 1.1.9 2 2 2h5v2h8v-2h5c1.1 0 2-.9 2-2V5c0-1.1-.9-2-2-2zm0 14H3V5h18v12z" />
      </svg>
    );
  // Generic / CastAudio / etc
  return (
    <svg viewBox="0 0 24 24" fill="currentColor" width="20" height="20">
      <path d="M12 3v9.28a4.39 4.39 0 00-1.5-.28C8.01 12 6 14.01 6 16.5S8.01 21 10.5 21c2.31 0 4.2-1.75 4.45-4H15V6h4V3h-7z" />
    </svg>
  );
}

export function DeviceSelector() {
  const { party, nowPlaying } = useParty();
  const [devices, setDevices] = useState<SpotifyDevice[]>([]);
  const [loading, setLoading] = useState(false);
  const [selecting, setSelecting] = useState<string | null>(null);
  const [open, setOpen] = useState(false);
  const [error, setError] = useState('');
  const panelRef = useRef<HTMLDivElement>(null);

  if (!party?.isHost || !party.spotifyConnected) return null;

  // Close on click outside
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

  const loadDevices = async () => {
    setLoading(true);
    setError('');
    try {
      const d = await api.getDevices();
      setDevices(d);
    } catch (err) {
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
    } catch (err: any) {
      setError(err.message || 'Failed to select device');
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
    <div className="device-selector" ref={panelRef}>
      <button onClick={toggleOpen} className={`device-btn ${open ? 'device-btn-open' : ''}`}>
        <svg viewBox="0 0 24 24" fill="currentColor" width="18" height="18">
          <path d="M17 2H7c-1.1 0-2 .9-2 2v16c0 1.1.9 2 2 2h10c1.1 0 2-.9 2-2V4c0-1.1-.9-2-2-2zm-5 2c1.1 0 2 .9 2 2s-.9 2-2 2-2-.9-2-2 .9-2 2-2zm0 16c-2.76 0-5-2.24-5-5s2.24-5 5-5 5 2.24 5 5-2.24 5-5 5zm0-8c-1.66 0-3 1.34-3 3s1.34 3 3 3 3-1.34 3-3-1.34-3-3-3z" />
        </svg>
        {activeDevice && <span className="device-btn-label">{activeDevice}</span>}
      </button>

      {open && (
        <div className="device-panel">
          <div className="device-panel-header">
            <span>Connect to a device</span>
            <button className="device-panel-close" onClick={() => setOpen(false)}>
              <svg viewBox="0 0 24 24" fill="currentColor" width="18" height="18">
                <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
              </svg>
            </button>
          </div>

          <div className="device-panel-body">
            {loading ? (
              <div className="device-panel-empty">Searching for devices...</div>
            ) : devices.length === 0 ? (
              <div className="device-panel-empty">
                No devices found.<br />Open Spotify on a device.
              </div>
            ) : (
              devices.map((d) => (
                <button
                  key={d.id}
                  className={`device-item ${d.isActive ? 'active' : ''}`}
                  onClick={(e) => handleSelect(e, d.id)}
                  disabled={selecting !== null}
                >
                  <span className={`device-item-icon ${d.isActive ? 'active' : ''}`}>
                    <DeviceIcon type={d.type} />
                  </span>
                  <span className="device-item-info">
                    <span className="device-item-name">{d.name}</span>
                    {d.isActive && <span className="device-item-status">Currently playing</span>}
                  </span>
                  {selecting === d.id && (
                    <span className="device-item-spinner">...</span>
                  )}
                </button>
              ))
            )}
          </div>

          {error && <div className="device-panel-error">{error}</div>}
        </div>
      )}
    </div>
  );
}
