import { useState, useEffect, useCallback } from 'react';
import { Link } from 'react-router-dom';
import { Search, Settings, Share2 } from 'lucide-react';
import { startAuthentication } from '@simplewebauthn/browser';
import type { PublicKeyCredentialRequestOptionsJSON } from '@simplewebauthn/browser';
import { api } from '../api/client';
import { useParty } from '../hooks/useParty';
import { useSearch } from '../hooks/useSearch';
import { NowPlaying } from '../components/NowPlaying';
import { QueueList } from '../components/QueueList';
import { SearchOverlay } from '../components/SearchOverlay';
import { HostControls } from '../components/HostControls';
import { DeviceSelector } from '../components/DeviceSelector';
import { BasePlaylistSelector } from '../components/BasePlaylistSelector';
import { ManagePanel } from '../components/ManagePanel';
import { ShareOverlay } from '../components/ShareOverlay';
import { PartyLayout } from '../components/PartyLayout';
import type { PanelDefinition } from '../components/PartyLayout';
import type { HostStatus, SavedPartySummary } from '../types';
import styles from './HostPortalPage.module.css';
import partyStyles from './PartyPage.module.css';

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

  // Active party UI state
  const [searchOpen, setSearchOpen] = useState(false);
  const [manageOpen, setManageOpen] = useState(false);
  const [shareOpen, setShareOpen] = useState(false);

  const checkStatus = useCallback(async () => {
    try {
      const s = await api.hostStatus();
      setStatus(s);
      if (s.authenticated) {
        const state = await api.getPartyState();
        if ('hasParty' in state && !state.hasParty) {
          setParty(null);
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
      <div className={styles.landing}>
        <h1 className={styles.title}>JukeVox</h1>
        <p className={styles.subtitle}>Host Portal</p>

        {status?.hasCredential ? (
          <div className={styles.panel}>
            <h2 className={styles.panelTitle}>Host Login</h2>
            <p className={styles.panelText}>
              Authenticate with your passkey to access the host portal.
            </p>
            <button
              className={styles.primaryBtn}
              onClick={handleLogin}
              disabled={authenticating}
            >
              {authenticating ? 'Authenticating...' : 'Login with Passkey'}
            </button>
            {authError && <p className={styles.error}>{authError}</p>}
          </div>
        ) : status?.setupAvailable ? (
          <div className={styles.panel}>
            <h2 className={styles.panelTitle}>Welcome</h2>
            <p className={styles.panelText}>
              No host credential found. Set up your passkey to get started.
            </p>
            <Link to="/host/setup" className={styles.setupLinkBtn}>
              Set Up Host Passkey
            </Link>
          </div>
        ) : (
          <div className={styles.panel}>
            <h2 className={styles.panelTitle}>Host Unavailable</h2>
            <p className={styles.panelText}>
              No host credential found and setup is not available. Set the{' '}
              <code>JUKEVOX_SETUP_TOKEN</code> environment variable to enable setup.
            </p>
          </div>
        )}

        <Link to="/" className={styles.bottomLink}>
          Back to Guest View
        </Link>
      </div>
    );
  }

  // Authenticated but no active party
  if (!party) {
    return (
      <div className={styles.landing}>
        <h1 className={styles.title}>JukeVox</h1>
        <p className={styles.subtitle}>Host Portal</p>

        {savedParty && (
          <div className={styles.resumeCard}>
            <h2 className={styles.resumeTitle}>You have an existing party</h2>
            <div className={styles.resumeDetails}>
              <span>Party ID: <strong>{savedParty.inviteCode}</strong></span>
              <span>{savedParty.queueCount} song{savedParty.queueCount !== 1 ? 's' : ''} in queue</span>
              <span>{savedParty.guestCount} guest{savedParty.guestCount !== 1 ? 's' : ''}</span>
            </div>
            <button className={styles.resumeBtn} onClick={handleResume} disabled={resuming}>
              {resuming ? 'Resuming...' : 'Resume Party'}
            </button>
          </div>
        )}

        <div className={styles.panel}>
          <form onSubmit={handleCreate}>
            <h2 className={styles.panelTitle}>Create a Party</h2>
            <input
              type="text"
              placeholder="Party ID (optional, auto-generated)"
              value={inviteCode}
              onChange={(e) => setInviteCode(e.target.value)}
              maxLength={10}
              className={styles.input}
            />
            <div className={styles.creditsRow}>
              <label className={styles.creditsLabel}>Credits per guest</label>
              <input
                type="number"
                min={1}
                max={100}
                value={defaultCredits}
                onChange={(e) => setDefaultCredits(parseInt(e.target.value, 10) || 5)}
                className={`${styles.input} ${styles.creditsInput}`}
              />
            </div>
            <button type="submit" disabled={creating} className={styles.primaryBtn}>
              {creating ? 'Creating...' : 'Create Party'}
            </button>
            {createError && <p className={styles.error}>{createError}</p>}
          </form>
        </div>

        <button className={styles.bottomLink} onClick={handleLogout}>
          Logout
        </button>
      </div>
    );
  }

  // Authenticated with active party - full host interface
  const handleCloseSearch = () => {
    setSearchOpen(false);
    setQuery('');
  };

  const handlePartyEnded = () => {
    setManageOpen(false);
    setParty(null);
  };

  const panels: PanelDefinition[] = [
    {
      label: 'Now Playing',
      first: true,
      content: (
        <NowPlaying>
          <HostControls />
        </NowPlaying>
      ),
    },
    {
      label: 'Queue',
      content: (
        <>
          <QueueList />
          <BasePlaylistSelector />
        </>
      ),
    },
    {
      label: 'Manage',
      desktopHidden: true,
      content: (active) => <ManagePanel mode="inline" visible={active} onPartyEnded={handlePartyEnded} />,
    },
  ];

  return (
    <PartyLayout
      headerTitle={
        <h1 className={partyStyles.headerTitle} style={{ width: 'auto', flex: 1 }}>JukeVox</h1>
      }
      headerRight={
        <>
          <button className={partyStyles.logoutBtn} onClick={handleLogout}>
            Logout
          </button>
          <div className={partyStyles.headerRight}>
            {!party.spotifyConnected && (
              <a href="/api/auth/login" className={partyStyles.connectBtn}>
                Connect Spotify
              </a>
            )}
            {party.spotifyConnected && (
              <span className={`${partyStyles.spotifyStatus} ${partyStyles.desktopOnly}`}>Spotify Connected</span>
            )}
            <DeviceSelector />
            <div className={partyStyles.headerIcons}>
              <button className={partyStyles.inviteCode} onClick={() => setShareOpen(true)} aria-label="Share party code">
                <span className={partyStyles.inviteCodeValue}>{party.inviteCode} <Share2 size={13} /></span>
                <span className={partyStyles.inviteCodeCompact}><Share2 size={18} /></span>
              </button>
              <button
                className={partyStyles.searchToggle}
                onClick={() => setSearchOpen(true)}
                aria-label="Search for a song"
              >
                <Search size={20} />
              </button>
              <button
                className={`${partyStyles.searchToggle} ${partyStyles.desktopOnly}`}
                onClick={() => setManageOpen(true)}
                aria-label="Manage party"
              >
                <Settings size={20} />
              </button>
            </div>
          </div>
        </>
      }
      panels={panels}
      overlays={
        <>
          <SearchOverlay
            open={searchOpen}
            onClose={handleCloseSearch}
            query={query}
            onQueryChange={setQuery}
            results={results}
            loading={searchLoading}
          />
          <ShareOverlay open={shareOpen} onClose={() => setShareOpen(false)} inviteCode={party.inviteCode} />
          {manageOpen && (
            <ManagePanel
              mode="overlay"
              onClose={() => setManageOpen(false)}
              onPartyEnded={handlePartyEnded}
            />
          )}
        </>
      }
    />
  );
}
