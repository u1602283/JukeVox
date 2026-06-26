import { createContext, useCallback, useEffect, useState } from 'react';
import type { ReactNode } from 'react';

export type Theme = 'light' | 'dark';

export interface ThemeContextValue {
  theme: Theme;
  toggleTheme: () => void;
  setTheme: (theme: Theme) => void;
}

const STORAGE_KEY = 'jukevox-theme';

// eslint-disable-next-line react-refresh/only-export-components
export const ThemeCtx = createContext<ThemeContextValue | null>(null);

function readInitialTheme(): Theme {
  // The inline bootstrap script in index.html sets data-theme before paint;
  // trust it so context and DOM stay in sync.
  const fromDom = document.documentElement.dataset.theme;
  if (fromDom === 'light' || fromDom === 'dark') return fromDom;
  return window.matchMedia?.('(prefers-color-scheme: light)').matches ? 'light' : 'dark';
}

function applyTheme(theme: Theme) {
  document.documentElement.dataset.theme = theme;
  const meta = document.querySelector('meta[name="theme-color"]');
  if (meta) meta.setAttribute('content', theme === 'light' ? '#f4f3ef' : '#0a0a0a');
}

export function ThemeProvider({ children }: { children: ReactNode }) {
  const [theme, setThemeState] = useState<Theme>(readInitialTheme);

  const setTheme = useCallback((next: Theme) => {
    setThemeState(next);
    applyTheme(next);
    try {
      localStorage.setItem(STORAGE_KEY, next);
    } catch {
      // ignore unavailable storage
    }
  }, []);

  const toggleTheme = useCallback(() => {
    setTheme(theme === 'light' ? 'dark' : 'light');
  }, [theme, setTheme]);

  // Follow OS changes only while the user has not made a manual choice.
  useEffect(() => {
    const mq = window.matchMedia('(prefers-color-scheme: light)');
    const onChange = (e: MediaQueryListEvent) => {
      let stored: string | null = null;
      try {
        stored = localStorage.getItem(STORAGE_KEY);
      } catch {
        // ignore
      }
      if (stored === 'light' || stored === 'dark') return;
      const next: Theme = e.matches ? 'light' : 'dark';
      setThemeState(next);
      applyTheme(next);
    };
    mq.addEventListener('change', onChange);
    return () => mq.removeEventListener('change', onChange);
  }, []);

  return <ThemeCtx.Provider value={{ theme, toggleTheme, setTheme }}>{children}</ThemeCtx.Provider>;
}
