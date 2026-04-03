import { useState, useEffect, useCallback } from 'react';
import { createPortal } from 'react-dom';
import { X, RefreshCw, Minus, Plus, UserX } from 'lucide-react';
import { api } from '../api/client';
import type { GuestInfo } from '../types';
import styles from './ManagePanel.module.css';

interface ManagePanelProps {
  mode: 'overlay' | 'inline';
  visible?: boolean;
  onClose?: () => void;
  onPartyEnded?: () => void;
}

function timeAgo(dateStr: string): string {
  const diff = Date.now() - new Date(dateStr).getTime();
  const mins = Math.floor(diff / 60000);
  if (mins < 1) return 'just now';
  if (mins < 60) return `${mins}m ago`;
  const hrs = Math.floor(mins / 60);
  return `${hrs}h ${mins % 60}m ago`;
}

export function ManagePanel({ mode, visible = true, onClose, onPartyEnded }: ManagePanelProps) {
  const [guests, setGuests] = useState<GuestInfo[]>([]);
  const [loading, setLoading] = useState(true);
  const [confirmEnd, setConfirmEnd] = useState(false);
  const [ending, setEnding] = useState(false);

  const fetchGuests = useCallback(async () => {
    setLoading(true);
    try {
      const data = await api.getGuests();
      setGuests(data);
    } catch {
      // ignore
    } finally {
      setLoading(false);
    }
  }, []);

  useEffect(() => {
    if (visible) fetchGuests();
  }, [visible, fetchGuests]);

  const handleSetCredits = async (sessionId: string, credits: number) => {
    const prev = guests;
    setGuests(gs => gs.map(g => g.sessionId === sessionId ? { ...g, creditsRemaining: Math.max(0, credits) } : g));
    try {
      const updated = await api.setGuestCredits(sessionId, credits);
      setGuests(gs => gs.map(g => g.sessionId === sessionId ? updated : g));
    } catch {
      setGuests(prev);
    }
  };

  const handleKick = async (sessionId: string) => {
    const prev = guests;
    setGuests(gs => gs.filter(g => g.sessionId !== sessionId));
    try {
      await api.kickGuest(sessionId);
    } catch {
      setGuests(prev);
    }
  };

  const handleBulkAdd = async (delta: number) => {
    const prev = guests;
    setGuests(gs => gs.map(g => ({ ...g, creditsRemaining: Math.max(0, g.creditsRemaining + delta) })));
    try {
      const updated = await api.addCreditsToAll(delta);
      setGuests(updated);
    } catch {
      setGuests(prev);
    }
  };

  const handleEndParty = async () => {
    setEnding(true);
    try {
      await api.endParty();
      onPartyEnded?.();
    } catch {
      setEnding(false);
    }
  };

  const isInline = mode === 'inline';

  const panel = (
    <>
      <div className={`${styles.panelHeader} ${isInline ? styles.inlinePanelHeader : ''}`}>
        <h2 className={styles.panelTitle}>Manage Party</h2>
        <button className={styles.refreshBtn} onClick={fetchGuests} aria-label="Refresh guests">
          <RefreshCw size={16} />
        </button>
        {mode === 'overlay' && onClose && (
          <button className={styles.closeBtn} onClick={onClose} aria-label="Close">
            <X size={20} />
          </button>
        )}
      </div>
      <div className={`${styles.content} ${isInline ? styles.inlineContent : ''}`}>
        <div className={styles.section}>
          <div className={styles.sectionTitle}>
            Guests ({guests.length})
          </div>
          {loading && guests.length === 0 ? (
            <div className={styles.empty}>Loading...</div>
          ) : guests.length === 0 ? (
            <div className={styles.empty}>No guests have joined yet</div>
          ) : (
            guests.map(guest => (
              <div key={guest.sessionId} className={styles.guestRow}>
                <div className={styles.guestInfo}>
                  <div className={styles.guestName}>{guest.displayName}</div>
                  <div className={styles.guestMeta}>Joined {timeAgo(guest.joinedAt)}</div>
                </div>
                <div className={styles.creditsControl}>
                  <button
                    className={styles.creditBtn}
                    onClick={() => handleSetCredits(guest.sessionId, guest.creditsRemaining - 1)}
                    disabled={guest.creditsRemaining <= 0}
                    aria-label="Decrease credits"
                  >
                    <Minus size={14} />
                  </button>
                  <span className={styles.creditValue}>{guest.creditsRemaining}</span>
                  <button
                    className={styles.creditBtn}
                    onClick={() => handleSetCredits(guest.sessionId, guest.creditsRemaining + 1)}
                    aria-label="Increase credits"
                  >
                    <Plus size={14} />
                  </button>
                </div>
                <button
                  className={styles.kickBtn}
                  onClick={() => handleKick(guest.sessionId)}
                  aria-label={`Kick ${guest.displayName}`}
                >
                  <UserX size={14} />
                </button>
              </div>
            ))
          )}
        </div>

        {guests.length > 0 && (
          <div className={styles.section}>
            <div className={styles.sectionTitle}>Bulk Credits</div>
            <div className={styles.bulkRow}>
              <span className={styles.bulkLabel}>Add to everyone</span>
              <button className={styles.bulkBtn} onClick={() => handleBulkAdd(1)}>+1</button>
              <button className={styles.bulkBtn} onClick={() => handleBulkAdd(5)}>+5</button>
              <button className={styles.bulkBtn} onClick={() => handleBulkAdd(-1)}>-1</button>
            </div>
          </div>
        )}

        <div className={styles.dangerSection}>
          <button className={styles.endBtn} onClick={() => setConfirmEnd(true)} disabled={ending}>
            End Party
          </button>
        </div>
      </div>

      {confirmEnd && createPortal(
        <div className={styles.confirmOverlay} onClick={() => setConfirmEnd(false)}>
          <div className={styles.confirmDialog} onClick={e => e.stopPropagation()}>
            <div className={styles.confirmTitle}>End Party?</div>
            <div className={styles.confirmText}>
              This will stop playback and disconnect all guests. This cannot be undone.
            </div>
            <div className={styles.confirmActions}>
              <button className={styles.confirmCancel} onClick={() => setConfirmEnd(false)}>
                Cancel
              </button>
              <button className={styles.confirmEnd} onClick={handleEndParty} disabled={ending}>
                {ending ? 'Ending...' : 'End Party'}
              </button>
            </div>
          </div>
        </div>,
        document.body
      )}
    </>
  );

  if (mode === 'overlay') {
    return (
      <>
        <div className={styles.scrim} onClick={onClose} />
        <div className={styles.overlay}>
          {panel}
        </div>
      </>
    );
  }

  return <div className={styles.inline}>{panel}</div>;
}
