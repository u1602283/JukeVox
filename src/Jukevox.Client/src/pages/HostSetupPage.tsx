import { useState, useEffect } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { startRegistration } from '@simplewebauthn/browser';
import type { PublicKeyCredentialCreationOptionsJSON } from '@simplewebauthn/browser';
import { api } from '../api/client';

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
      <div className="landing-page">
        <h1>JukeVox</h1>
        <p className="subtitle">Host Setup Complete</p>

        <div className="panel setup-success">
          <h2>Passkey Registered</h2>
          <p className="setup-info">
            Your host passkey has been saved. To persist across redeployments,
            add this DNS TXT record:
          </p>
          <div className="dns-record">
            <code>{`_jukevox-auth.yourdomain.com TXT "${dnsRecord}"`}</code>
          </div>
          <Link to="/host" className="setup-continue-btn">
            Continue to Host Portal
          </Link>
        </div>
      </div>
    );
  }

  return (
    <div className="landing-page">
      <h1>JukeVox</h1>
      <p className="subtitle">Host Setup</p>

      <div className="panel">
        <form onSubmit={handleRegister} className="setup-form">
          <h2>Register Host Passkey</h2>
          <p className="setup-info">
            Enter the setup token to register your passkey. This is a one-time
            process.
          </p>
          <input
            type="text"
            placeholder="Setup token (e.g. Crimson-Tiger-7-Plasma-Moon)"
            value={token}
            onChange={(e) => setToken(e.target.value)}
            autoComplete="off"
          />
          <button type="submit" disabled={registering || !token.trim()}>
            {registering ? 'Registering...' : 'Register Passkey'}
          </button>
          {error && <p className="error">{error}</p>}
        </form>
      </div>

      <Link to="/host" className="host-login-link">
        Already registered? Login
      </Link>
    </div>
  );
}
