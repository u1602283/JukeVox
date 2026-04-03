import { useContext } from 'react';
import { PartyCtx } from '../context/PartyContext';
import type { PartyContextValue } from '../context/PartyContext';

export type { PartyContextValue };

export function useParty(): PartyContextValue {
  const ctx = useContext(PartyCtx);
  if (!ctx) throw new Error('useParty must be used within PartyProvider');
  return ctx;
}
