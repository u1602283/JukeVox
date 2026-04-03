import { Link } from 'react-router-dom';
import { ArrowRight } from 'lucide-react';
import styles from './GuestLandingPage.module.css';

export function GuestLandingPage() {
  return (
    <div className={styles.page}>
      <h1 className={styles.title}>JukeVox</h1>
      <p className={styles.subtitle}>Semi-democratic music queue management</p>

      <div className={styles.panel}>
        <p className={styles.info}>Ask the host to share a join link or scan the QR code to join a party.</p>
      </div>

      <Link to="/host" className={styles.hostLink}>
        Host Login
        <ArrowRight size={14} className={styles.hostLinkArrow} />
      </Link>
    </div>
  );
}
