import { useState, useEffect } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { startRegistration } from '@simplewebauthn/browser';
import type { PublicKeyCredentialCreationOptionsJSON } from '@simplewebauthn/browser';
import { api } from '../api/client';
import styles from './HostSetupPage.module.css';

export function HostSetupPage() {
  const navigate = useNavigate();
  const [available, setAvailable] = useState<boolean | null>(null);
  const [token, setToken] = useState('');
  const [registering, setRegistering] = useState(false);
  const [dnsRecord, setDnsRecord] = useState<string | null>(null);
  const [error, setError] = useState('');

  useEffect(() => {
    api.hostSetupStatus()
      .then((data) => {
        if (!data.available) navigate('/host', { replace: true });
        else setAvailable(true);
      })
      .catch(() => navigate('/host', { replace: true }));
  }, [navigate]);

  const handleRegister = async (e: React.FormEvent) => {
    e.preventDefault();
    if (!token.trim()) return;

    setRegistering(true);
    setError('');

    try {
      const options = await api.hostSetupBegin(token.trim());
      const attestation = await startRegistration({
        optionsJSON: options as unknown as PublicKeyCredentialCreationOptionsJSON,
      });
      const result = await api.hostSetupComplete(attestation);
      setDnsRecord(result.dnsRecord);
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Registration failed';
      setError(message);
    } finally {
      setRegistering(false);
    }
  };

  if (available === null) {
    return <div className="loading">Loading...</div>;
  }

  if (dnsRecord) {
    return (
      <div className={styles.page}>
        <h1 className={styles.title}>JukeVox</h1>
        <p className={styles.subtitle}>Host Setup Complete</p>

        <div className={`${styles.panel} ${styles.successPanel}`}>
          <h2 className={styles.panelTitle}>Passkey Registered</h2>
          <p className={styles.panelText}>
            Your host passkey has been saved. To persist across redeployments,
            add this DNS TXT record:
          </p>
          <div className={styles.dnsRecord}>
            <code>{`_jukevox-auth.yourdomain.com TXT "${dnsRecord}"`}</code>
          </div>
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
      <p className={styles.subtitle}>Host Setup</p>

      <div className={styles.panel}>
        <form onSubmit={handleRegister}>
          <h2 className={styles.panelTitle}>Register Host Passkey</h2>
          <p className={styles.panelText}>
            Enter the setup token to register your passkey. This is a one-time
            process.
          </p>
          <input
            type="text"
            placeholder="Setup token from server console"
            value={token}
            onChange={(e) => setToken(e.target.value)}
            autoComplete="off"
            className={styles.input}
          />
          <button
            type="submit"
            disabled={registering || !token.trim()}
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
