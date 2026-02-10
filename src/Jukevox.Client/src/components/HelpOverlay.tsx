import { X } from 'lucide-react';
import styles from './HelpOverlay.module.css';

interface HelpOverlayProps {
  open: boolean;
  onClose: () => void;
}

export function HelpOverlay({ open, onClose }: HelpOverlayProps) {
  if (!open) return null;

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
        <div className={styles.content} data-scrollable>
          <p className={styles.intro}>
            Welcome to JukeVox — semi-democratised music queue management.
            The host controls the party, but you get a say. Sort of.
          </p>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Adding Songs</h3>
            <p>
              Tap the search icon in the header, find a song, and add it to the queue.
              Revolutionary stuff, we know. Your song lands ahead of any
              background playlist tracks, so it <em>will</em> get played — eventually.
            </p>
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Credits</h3>
            <p>
              Each song you queue costs one credit. When you're out, you're out — no
              begging the app will help. The host decides how many credits you get and
              can grant more or revoke them at any time, for any reason, with zero
              accountability. Democracy has its limits.
            </p>
          </section>

          <section className={styles.section}>
            <h3 className={styles.sectionTitle}>Voting</h3>
            <p>
              Every queued song can be upvoted or downvoted. Upvotes push a song
              higher in the queue — one vote, one position. Downvotes don't move
              anything down (we're not monsters), but if a song hits -3 it gets
              removed entirely. Consider it a collective veto.
            </p>
            <p>
              If a background playlist track somehow gets 3 or more upvotes,
              it'll be promoted to the very top of the queue. The people have spoken.
            </p>
          </section>
        </div>
      </div>
    </>
  );
}
