import { useParty } from '../context/PartyContext';

export function CreditsBadge() {
  const { party, credits } = useParty();

  if (party?.isHost || credits === null) return null;

  return (
    <div className={`credits-badge ${credits === 0 ? 'empty' : ''}`}>
      {credits} credit{credits !== 1 ? 's' : ''} remaining
    </div>
  );
}
