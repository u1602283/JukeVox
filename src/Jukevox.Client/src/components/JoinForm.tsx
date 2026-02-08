import { useState } from 'react';
import { api } from '../api/client';
import { useParty } from '../hooks/useParty';
import styles from './JoinForm.module.css';

export function JoinForm() {
  const { setParty, setQueue, setCredits } = useParty();
  const [inviteCode, setInviteCode] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [error, setError] = useState('');
  const [loading, setLoading] = useState(false);

  const handleJoin = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inviteCode.trim() || !displayName.trim()) return;

    setLoading(true);
    setError('');

    try {
      const state = await api.joinParty({
        inviteCode: inviteCode.trim(),
        displayName: displayName.trim(),
      });
      setParty(state);
      setQueue(state.queue);
      if (state.creditsRemaining !== undefined) {
        setCredits(state.creditsRemaining);
      }
    } catch (err: unknown) {
      setError(err instanceof Error ? err.message : 'Failed to join party');
    } finally {
      setLoading(false);
    }
  };

  return (
    <form onSubmit={handleJoin} className={styles.form}>
      <h2 className={styles.title}>Join a Party</h2>
      <input
        type="text"
        placeholder="Invite Code"
        value={inviteCode}
        onChange={(e) => setInviteCode(e.target.value)}
        maxLength={10}
        required
        className={styles.input}
      />
      <input
        type="text"
        placeholder="Your Name"
        value={displayName}
        onChange={(e) => setDisplayName(e.target.value)}
        maxLength={30}
        required
        className={styles.input}
      />
      <button type="submit" disabled={loading} className={styles.submitBtn}>
        {loading ? 'Joining...' : 'Join Party'}
      </button>
      {error && <p className={styles.error}>{error}</p>}
    </form>
  );
}
