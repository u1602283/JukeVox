import { useState, useEffect } from 'react';
import { api } from '../api/client';
import { useParty } from '../context/PartyContext';
import { JoinForm } from '../components/JoinForm';
import type { SavedPartySummary } from '../types';

export function LandingPage() {
  const { setParty } = useParty();
  const [inviteCode, setInviteCode] = useState('');
  const [defaultCredits, setDefaultCredits] = useState(5);
  const [creating, setCreating] = useState(false);
  const [error, setError] = useState('');
  const [savedParty, setSavedParty] = useState<SavedPartySummary | null>(null);
  const [resuming, setResuming] = useState(false);
  const [dismissed, setDismissed] = useState(false);

  useEffect(() => {
    api.getSavedParty().then((data) => {
      if (data.exists) setSavedParty(data);
    }).catch(() => {});
  }, []);

  const handleResume = async () => {
    setResuming(true);
    setError('');
    try {
      const state = await api.resumeParty();
      setParty(state);
    } catch (err: any) {
      setError(err.message || 'Failed to resume party');
    } finally {
      setResuming(false);
    }
  };

  const handleCreate = async (e: React.FormEvent) => {
    e.preventDefault();
    setCreating(true);
    setError('');

    try {
      const state = await api.createParty({
        inviteCode: inviteCode.trim() || undefined,
        defaultCredits,
      });
      setParty(state);
    } catch (err: any) {
      setError(err.message || 'Failed to create party');
    } finally {
      setCreating(false);
    }
  };

  return (
    <div className="landing-page">
      <h1>Party Queue</h1>
      <p className="subtitle">Collaborative music for your party</p>

      {savedParty && !dismissed && (
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
          <button className="resume-dismiss" onClick={() => setDismissed(true)}>
            Start New Instead
          </button>
          {error && <p className="error">{error}</p>}
        </div>
      )}

      <div className="landing-panels">
        <div className="panel">
          <form onSubmit={handleCreate} className="create-form">
            <h2>Host a Party</h2>
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
            {error && !savedParty && <p className="error">{error}</p>}
          </form>
        </div>

        <div className="divider">or</div>

        <div className="panel">
          <JoinForm />
        </div>
      </div>
    </div>
  );
}
