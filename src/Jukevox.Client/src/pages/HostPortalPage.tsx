import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { startAuthentication } from '@simplewebauthn/browser';
import type { PublicKeyCredentialRequestOptionsJSON } from '@simplewebauthn/browser';
import { api } from '../api/client';
import { useParty } from '../context/PartyContext';
import { useSearch } from '../hooks/useSearch';
import { NowPlaying } from '../components/NowPlaying';
import { QueueList } from '../components/QueueList';
import { SearchBar } from '../components/SearchBar';
import { SearchResults } from '../components/SearchResults';
import { HostControls } from '../components/HostControls';
import { DeviceSelector } from '../components/DeviceSelector';
import { BasePlaylistSelector } from '../components/BasePlaylistSelector';
import type { HostStatus, SavedPartySummary } from '../types';

export function HostPortalPage() {
  const { party, setParty } = useParty();
  const { query, setQuery, results, loading: searchLoading } = useSearch();

  const [status, setStatus] = useState<HostStatus | null>(null);
  const [authenticating, setAuthenticating] = useState(false);
  const [authError, setAuthError] = useState('');
  const [checkingStatus, setCheckingStatus] = useState(true);

  // Party creation state
  const [inviteCode, setInviteCode] = useState('');
  const [defaultCredits, setDefaultCredits] = useState(5);
  const [creating, setCreating] = useState(false);
  const [createError, setCreateError] = useState('');
  const [savedParty, setSavedParty] = useState<SavedPartySummary | null>(null);
  const [resuming, setResuming] = useState(false);

  const checkStatus = useCallback(async () => {
    try {
      const s = await api.hostStatus();
      setStatus(s);
      if (s.authenticated) {
        // Load party state if authenticated
        const state = await api.getPartyState();
        if ('hasParty' in state && !state.hasParty) {
          setParty(null);
          // Check for saved party to resume
          try {
            const saved = await api.getSavedParty();
            if (saved.exists) setSavedParty(saved);
          } catch { /* no saved party */ }
        } else {
          setParty(state);
        }
      }
    } catch {
      setStatus({ authenticated: false, hasCredential: false, setupAvailable: false });
    } finally {
      setCheckingStatus(false);
    }
  }, [setParty]);

  useEffect(() => {
    checkStatus();
  }, [checkStatus]);

  const handleLogin = async () => {
    setAuthenticating(true);
    setAuthError('');
    try {
      const options = await api.hostLoginBegin();
      const assertion = await startAuthentication({
        optionsJSON: options as unknown as PublicKeyCredentialRequestOptionsJSON,
      });
      await api.hostLoginComplete(assertion);
      await checkStatus();
    } catch (err: unknown) {
      const message = err instanceof Error ? err.message : 'Authentication failed';
      setAuthError(message);
    } finally {
      setAuthenticating(false);
    }
  };

  const handleLogout = async () => {
    await api.hostLogout();
    setStatus({ authenticated: false, hasCredential: true, setupAvailable: false });
    setParty(null);
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreating(true);
    setCreateError('');
    try {
      const state = await api.createParty({
        inviteCode: inviteCode.trim() || undefined,
        defaultCredits,
      });
      setParty(state);
    } catch (err: unknown) {
      setCreateError(err instanceof Error ? err.message : 'Failed to create party');
    } finally {
      setCreating(false);
    }
  };

  const handleResume = async () => {
    setResuming(true);
    setCreateError('');
    try {
      const state = await api.resumeParty();
      setParty(state);
    } catch (err: unknown) {
      setCreateError(err instanceof Error ? err.message : 'Failed to resume party');
    } finally {
      setResuming(false);
    }
  };

  if (checkingStatus) {
    return <div className="loading">Loading...</div>;
  }

  // Not authenticated - show login or setup link
  if (!status?.authenticated) {
    return (
      <div className="landing-page">
        <h1>JukeVox</h1>
        <p className="subtitle">Host Portal</p>

        {status?.hasCredential ? (
          <div className="panel">
            <h2>Host Login</h2>
            <p className="setup-info">
              Authenticate with your passkey to access the host portal.
            </p>
            <button
              className="login-btn"
              onClick={handleLogin}
              disabled={authenticating}
            >
              {authenticating ? 'Authenticating...' : 'Login with Passkey'}
            </button>
            {authError && <p className="error">{authError}</p>}
          </div>
        ) : status?.setupAvailable ? (
          <div className="panel">
            <h2>Welcome</h2>
            <p className="setup-info">
              No host credential found. Set up your passkey to get started.
            </p>
            <Link to="/host/setup" className="setup-link-btn">
              Set Up Host Passkey
            </Link>
          </div>
        ) : (
          <div className="panel">
            <h2>Host Unavailable</h2>
            <p className="setup-info">
              No host credential found and setup is not available. Set the{' '}
              <code>JUKEVOX_SETUP_TOKEN</code> environment variable to enable setup.
            </p>
          </div>
        )}

        <Link to="/" className="host-login-link">
          Back to Guest View
        </Link>
      </div>
    );
  }

  // Authenticated but no active party
  if (!party) {
    return (
      <div className="landing-page">
        <h1>JukeVox</h1>
        <p className="subtitle">Host Portal</p>

        {savedParty && (
          <div className="resume-card">
            <h2>You have an existing party</h2>
            <div className="resume-details">
              <span>Code: <strong>{savedParty.inviteCode}</strong></span>
              <span>{savedParty.queueCount} song{savedParty.queueCount !== 1 ? 's' : ''} in queue</span>
              <span>{savedParty.guestCount} guest{savedParty.guestCount !== 1 ? 's' : ''}</span>
            </div>
            <button className="resume-btn" onClick={handleResume} disabled={resuming}>
              {resuming ? 'Resuming...' : 'Resume Party'}
            </button>
          </div>
        )}

        <div className="panel">
          <form onSubmit={handleCreate} className="create-form">
            <h2>Create a Party</h2>
            <input
              type="text"
              placeholder="Invite Code (optional, auto-generated)"
              value={inviteCode}
              onChange={(e) => setInviteCode(e.target.value)}
              maxLength={10}
            />
            <div className="credits-input">
              <label>Credits per guest</label>
              <input
                type="number"
                min={1}
                max={100}
                value={defaultCredits}
                onChange={(e) => setDefaultCredits(parseInt(e.target.value, 10) || 5)}
              />
            </div>
            <button type="submit" disabled={creating}>
              {creating ? 'Creating...' : 'Create Party'}
            </button>
            {createError && <p className="error">{createError}</p>}
          </form>
        </div>

        <button className="host-login-link" onClick={handleLogout}>
          Logout
        </button>
      </div>
    );
  }

  // Authenticated with active party - full host interface
  return (
    <div className="party-page">
      <header className="party-header">
        <h1>JukeVox</h1>
        <div className="party-info">
          <span className="invite-code">Code: <strong>{party.inviteCode}</strong></span>
          {!party.spotifyConnected && (
            <a href="/api/auth/login" className="connect-btn">
              Connect Spotify
            </a>
          )}
          {party.spotifyConnected && (
            <span className="spotify-status connected">Spotify Connected</span>
          )}
        </div>
      </header>

      <NowPlaying />

      <div className="host-section">
        <HostControls />
        <DeviceSelector />
        <BasePlaylistSelector />
      </div>

      <div className="search-section">
        <SearchBar query={query} onQueryChange={setQuery} loading={searchLoading} />
        <SearchResults results={results} />
      </div>

      <QueueList />

      <div className="host-portal-footer">
        <button className="host-login-link" onClick={handleLogout}>
          Logout
        </button>
      </div>
    </div>
  );
}
