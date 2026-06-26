import { useContext } from 'react';
import { ThemeCtx } from '../context/ThemeContext';
import type { ThemeContextValue } from '../context/ThemeContext';

export type { ThemeContextValue };

export function useTheme(): ThemeContextValue {
  const ctx = useContext(ThemeCtx);
  if (!ctx) throw new Error('useTheme must be used within ThemeProvider');
  return ctx;
}
