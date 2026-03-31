import { useState } from 'react';
import { X } from 'lucide-react';
import { api } from '../api/client';
import { useParty } from '../hooks/useParty';
import styles from './HelpOverlay.module.css';

interface HelpOverlayProps {
  open: boolean;
  onClose: () => void;
}

export function HelpOverlay({ open, onClose }: HelpOverlayProps) {
  const { setParty } = useParty();
  const [leaving, setLeaving] = useState(false);

  if (!open) return null;

  const handleLeave = async () => {
    if (!confirm('Leave this party? You can rejoin with the same link.')) return;
    setLeaving(true);
    try {
      await api.leaveParty();
      setParty(null);
      window.location.href = '/';
    } catch {
      setLeaving(false);
    }
  };

  return (
    <>
      <div className={styles.scrim} onClick={onClose} />
      <div className={styles.overlay}>
        <div className={styles.header}>
          <h2 className={styles.title}>How JukeVox Works</h2>
          <button className={styles.closeBtn} onClick={onClose} aria-label="Close">
            <X size={20} />
          </button>
        </div>
        <div className={styles.content}>
          <blockquote className={styles.quote}>
            "I love democracy."
            <footer className={styles.quoteAttribution}>- Supreme Chancellor Sheev Palpatine</footer>
          </blockquote>
          <p className={styles.intro}>
            Welcome to JukeVox - semi-democratised music queue management.
            The host controls the party, but you get a say. Sort of.
          </p>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Adding Songs</h3>
            <p>
              Tap the search icon in the header, find a song, and add it to the queue.
              Groundbreaking, right? Your song lands ahead of any
              background playlist tracks, so it <em>will</em> get played - eventually (probably).
            </p>
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Credits</h3>
            <p>
              Each song you queue costs one credit. When you're out, you're out - no
              begging the app will help. The host decides how many credits you get and
              can grant more or revoke them at any time, for any reason, with zero
              accountability. Democracy has its limits.
            </p>
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Voting</h3>
            <p>
              Every queued song can be upvoted or downvoted. Any song
              with at least 3 upvotes shoots to the top of the queue.
              The people have spoken.
            </p>
            <p>
              On the flip side, if a song hits -3 it gets removed entirely.
              No refund on the credit, either - consider it a lesson in
              taste. Below that threshold, votes are
              purely decorative - a gentle nod of approval or quiet disapproval
              that changes absolutely nothing. Yet.
            </p>
            <p>
              The host can also drag the queue into whatever order they like,
              and that overrides everything. Benevolent dictatorship with
              democratic characteristics.
            </p>
          </section>

          <section className={styles.section}>
            <button
              className={styles.leaveBtn}
              onClick={handleLeave}
              disabled={leaving}
            >
              {leaving ? 'Leaving...' : 'Leave Party'}
            </button>
          </section>
        </div>
      </div>
    </>
  );
}
