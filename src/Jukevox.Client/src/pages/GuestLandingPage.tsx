import { Link } from 'react-router-dom';
import { ArrowRight } from 'lucide-react';
import { JoinForm } from '../components/JoinForm';
import styles from './GuestLandingPage.module.css';

export function GuestLandingPage() {
  return (
    <div className={styles.page}>
      <h1 className={styles.title}>JukeVox</h1>
      <p className={styles.subtitle}>Semi-democratic music queue management</p>

      <div className={styles.panel}>
        <JoinForm />
      </div>

      <Link to="/host" className={styles.hostLink}>
        Host Login
        <ArrowRight size={14} className={styles.hostLinkArrow} />
      </Link>
    </div>
  );
}
