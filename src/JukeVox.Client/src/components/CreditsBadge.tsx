import { useRef, useEffect } from 'react';
import { useParty } from '../hooks/useParty';
import tokenIcon from '../assets/token.svg';
import styles from './CreditsBadge.module.css';

export function CreditsBadge() {
  const { party, credits } = useParty();
  const badgeRef = useRef<HTMLDivElement>(null);
  const prevCredits = useRef(credits);

  useEffect(() => {
    if (credits !== prevCredits.current && badgeRef.current) {
      badgeRef.current.classList.remove(styles.bounce);
      // Force reflow
      void badgeRef.current.offsetWidth;
      badgeRef.current.classList.add(styles.bounce);
    }
    prevCredits.current = credits;
  }, [credits]);

  if (party?.isHost || credits === null) return null;

  return (
    <div
      ref={badgeRef}
      className={`${styles.badge} ${credits === 0 ? styles.empty : ''}`}
    >
      <img className={styles.icon} src={tokenIcon} alt="" />
      <span className={styles.labelFull}>{credits} credit{credits !== 1 ? 's' : ''} remaining</span>
      <span className={styles.labelCompact}>{credits} credit{credits !== 1 ? 's' : ''}</span>
    </div>
  );
}
