import { useState, useEffect } from 'react';
import { useNavigate, Link } from 'react-router-dom';
import { api } from '../api/client';
import type { HostInfo } from '../types';
import styles from './HostPortalPage.module.css';
import adminStyles from './HostAdminPage.module.css';

function formatDate(iso: string): string {
  const d = new Date(iso);
  return d.toLocaleDateString(undefined, { day: 'numeric', month: 'short', year: 'numeric' });
}

export function HostAdminPage() {
  const navigate = useNavigate();
  const [loading, setLoading] = useState(true);
  const [hosts, setHosts] = useState<HostInfo[]>([]);
  const [deletingHostId, setDeletingHostId] = useState<string | null>(null);
  const [generatedCode, setGeneratedCode] = useState<string | null>(null);
  const [generatingCode, setGeneratingCode] = useState(false);

  useEffect(() => {
    api.hostStatus().then(s => {
      if (!s.authenticated || !s.isAdmin) {
        navigate('/host', { replace: true });
        return;
      }
      api.getHosts().then(setHosts).catch(() => {});
    }).catch(() => {
      navigate('/host', { replace: true });
    }).finally(() => setLoading(false));
  }, [navigate]);

  const handleGenerateInviteCode = async () => {
    setGeneratingCode(true);
    try {
      const result = await api.generateInviteCode();
      setGeneratedCode(result.code);
    } catch {
      // ignore
    } finally {
      setGeneratingCode(false);
    }
  };

  const handleDeleteHost = async (targetHostId: string) => {
    if (!confirm('Remove this host credential? They will no longer be able to log in.')) return;
    setDeletingHostId(targetHostId);
    try {
      await api.deleteHost(targetHostId);
      setHosts(prev => prev.filter(h => h.hostId !== targetHostId));
    } catch {
      // ignore
    } finally {
      setDeletingHostId(null);
    }
  };

  if (loading) {
    return <div className="loading">Loading...</div>;
  }

  const nonAdminHosts = hosts.filter(h => !h.isAdmin);

  return (
    <div className={styles.landing}>
      <h1 className={styles.title}>JukeVox</h1>
      <p className={styles.subtitle}>Admin</p>

      <div className={styles.panel}>
        <h2 className={styles.panelTitle}>Invite Code</h2>
        <p className={styles.panelText}>
          Generate a one-time code for a new host to register. Only one code is valid at a time.
        </p>
        <button
          className={styles.primaryBtn}
          onClick={handleGenerateInviteCode}
          disabled={generatingCode}
          style={{ marginBottom: generatedCode ? '0.75rem' : 0 }}
        >
          {generatingCode ? 'Generating...' : 'Generate Invite Code'}
        </button>
        {generatedCode && (
          <div className={adminStyles.codeDisplay}>
            {generatedCode}
          </div>
        )}
      </div>

      <div className={styles.panel}>
        <h2 className={styles.panelTitle}>Registered Hosts</h2>
        {nonAdminHosts.length === 0 ? (
          <p className={styles.panelText}>No other hosts registered yet.</p>
        ) : (
          <table className={adminStyles.table}>
            <thead>
              <tr>
                <th>Name</th>
                <th>Registered</th>
                <th></th>
              </tr>
            </thead>
            <tbody>
              {nonAdminHosts.map(h => (
                <tr key={h.hostId}>
                  <td>{h.displayName}</td>
                  <td className={adminStyles.datecell}>{formatDate(h.createdAt)}</td>
                  <td className={adminStyles.actionCell}>
                    <button
                      className={adminStyles.removeBtn}
                      onClick={() => handleDeleteHost(h.hostId)}
                      disabled={deletingHostId === h.hostId}
                    >
                      {deletingHostId === h.hostId ? '...' : 'Remove'}
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>

      <Link to="/host" className={styles.bottomLink}>
        Back to Host Portal
      </Link>
    </div>
  );
}
