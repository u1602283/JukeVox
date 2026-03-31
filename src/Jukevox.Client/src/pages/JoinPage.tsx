import { useState, useEffect } from 'react';
import { useParams, Navigate, useNavigate } from 'react-router-dom';
import { api } from '../api/client';
import { useParty } from '../hooks/useParty';
import styles from './GuestLandingPage.module.css';
import formStyles from '../components/JoinForm.module.css';

export function JoinPage() {
  const { token } = useParams<{ token: string }>();
  const navigate = useNavigate();
  const { party, setParty, setQueue, setCredits, loading: partyLoading } = useParty();
  const [displayName, setDisplayName] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);
  const [checking, setChecking] = useState(true);

  useEffect(() => {
    api.hostStatus()
      .then(s => {
        if (s.authenticated) {
          navigate('/host', { replace: true });
        } else {
          setChecking(false);
        }
      })
      .catch(() => setChecking(false));
  }, [navigate]);

  if (!token) return <Navigate to="/" replace />;
  if (checking || partyLoading) return <div className="loading">Loading...</div>;

  // Already in this party as a guest — go straight to the party view
  if (party && !party.isHost && party.joinToken === token) {
    return <Navigate to="/" replace />;
  }

  const handleLeaveAndJoin = async () => {
    setLoading(true);
    setError('');
    try {
      await api.leaveParty();
      setParty(null);
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to leave party');
    } finally {
      setLoading(false);
    }
  };

  const handleJoin = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!displayName.trim()) return;

    setLoading(true);
    setError('');

    try {
      const state = await api.joinParty({
        joinToken: token,
        displayName: displayName.trim(),
      });
      setParty(state);
      setQueue(state.queue);
      if (state.creditsRemaining !== undefined) {
        setCredits(state.creditsRemaining);
      }
      navigate('/', { replace: true });
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to join party');
    } finally {
      setLoading(false);
    }
  };

  // Already in a different party — prompt to leave first
  if (party && !party.isHost) {
    return (
      <div className={styles.page}>
        <h1 className={styles.title}>JukeVox</h1>
        <p className={styles.subtitle}>Semi-democratic music queue management</p>

        <div className={styles.panel}>
          <div className={formStyles.form}>
            <h2 className={formStyles.title}>Switch Party?</h2>
            <p className={styles.info}>
              You're already in a party{party.displayName ? ` as ${party.displayName}` : ''}.
              Leave it to join this one?
            </p>
            <button
              onClick={handleLeaveAndJoin}
              disabled={loading}
              className={formStyles.submitBtn}
            >
              {loading ? 'Leaving...' : 'Leave & Continue'}
            </button>
            <button
              onClick={() => navigate('/', { replace: true })}
              className={formStyles.submitBtn}
              style={{ background: 'transparent', border: '1px solid var(--glass-border)', color: 'var(--text-secondary)' }}
            >
              Stay in Current Party
            </button>
            {error && <p className={formStyles.error}>{error}</p>}
          </div>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.page}>
      <h1 className={styles.title}>JukeVox</h1>
      <p className={styles.subtitle}>Semi-democratic music queue management</p>

      <div className={styles.panel}>
        <form onSubmit={handleJoin} className={formStyles.form}>
          <h2 className={formStyles.title}>Join Party</h2>
          <input
            type="text"
            placeholder="Your Name"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            maxLength={30}
            required
            className={formStyles.input}
          />
          <button type="submit" disabled={loading} className={formStyles.submitBtn}>
            {loading ? 'Joining...' : 'Join Party'}
          </button>
          {error && <p className={formStyles.error}>{error}</p>}
        </form>
      </div>
    </div>
  );
}
