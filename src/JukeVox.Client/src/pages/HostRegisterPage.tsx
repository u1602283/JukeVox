import { useState, useEffect } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { startRegistration } from '@simplewebauthn/browser';
import type { PublicKeyCredentialCreationOptionsJSON } from '@simplewebauthn/browser';
import { api } from '../api/client';
import styles from './HostSetupPage.module.css';

export function HostRegisterPage() {
  const navigate = useNavigate();
  const [inviteCode, setInviteCode] = useState('');
  const [displayName, setDisplayName] = useState('');
  const [registering, setRegistering] = useState(false);
  const [success, setSuccess] = useState(false);
  const [error, setError] = useState('');

  useEffect(() => {
    // If no credentials exist, redirect to setup
    api.hostStatus().then((s) => {
      if (!s.hasCredential && s.setupAvailable) {
        navigate('/host/setup', { replace: true });
      }
    }).catch(() => {});
  }, [navigate]);

  const handleRegister = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!inviteCode.trim() || !displayName.trim()) return;

    setRegistering(true);
    setError('');

    try {
      const options = await api.hostRegisterBegin(inviteCode.trim(), displayName.trim());
      const attestation = await startRegistration({
        optionsJSON: options as unknown as PublicKeyCredentialCreationOptionsJSON,
      });
      await api.hostRegisterComplete(attestation);
      setSuccess(true);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Registration failed';
      setError(message);
    } finally {
      setRegistering(false);
    }
  };

  if (success) {
    return (
      <div className={styles.page}>
        <h1 className={styles.title}>JukeVox</h1>
        <p className={styles.subtitle}>Registration Complete</p>

        <div className={`${styles.panel} ${styles.successPanel}`}>
          <h2 className={styles.panelTitle}>Passkey Registered</h2>
          <p className={styles.panelText}>
            Your host passkey has been saved. You can now create and manage parties.
          </p>
          <Link to="/host" className={styles.continueBtn}>
            Continue to Host Portal
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className={styles.page}>
      <h1 className={styles.title}>JukeVox</h1>
      <p className={styles.subtitle}>Host Registration</p>

      <div className={styles.panel}>
        <form onSubmit={handleRegister}>
          <h2 className={styles.panelTitle}>Register as Host</h2>
          <p className={styles.panelText}>
            Enter your invite code and choose a display name to register your passkey.
          </p>
          <input
            type="text"
            placeholder="Invite code"
            value={inviteCode}
            onChange={(e) => setInviteCode(e.target.value)}
            autoComplete="off"
            className={styles.input}
          />
          <input
            type="text"
            placeholder="Display name"
            value={displayName}
            onChange={(e) => setDisplayName(e.target.value)}
            maxLength={30}
            className={styles.input}
          />
          <button
            type="submit"
            disabled={registering || !inviteCode.trim() || !displayName.trim()}
            className={styles.primaryBtn}
          >
            {registering ? 'Registering...' : 'Register Passkey'}
          </button>
          {error && <p className={styles.error}>{error}</p>}
        </form>
      </div>

      <Link to="/host" className={styles.bottomLink}>
        Already registered? Login
      </Link>
    </div>
  );
}
