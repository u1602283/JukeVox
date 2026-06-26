import { Sun, Moon } from 'lucide-react';
import { useTheme } from '../hooks/useTheme';
import styles from './ThemeToggle.module.css';

interface ThemeToggleProps {
  /** 'icon' = single round button (header); 'row' = labeled segmented control (overlays/mobile). */
  variant?: 'icon' | 'row';
  /** Extra class applied to the icon-variant button (e.g. header sizing / desktopOnly). */
  className?: string;
}

export function ThemeToggle({ variant = 'icon', className = '' }: ThemeToggleProps) {
  const { theme, toggleTheme, setTheme } = useTheme();

  if (variant === 'row') {
    return (
      <div className={styles.row}>
        <span className={styles.rowLabel}>Appearance</span>
        <div className={styles.segment} role="group" aria-label="Theme">
          <button
            type="button"
            className={`${styles.segmentBtn} ${theme === 'light' ? styles.segmentActive : ''}`}
            aria-pressed={theme === 'light'}
            onClick={() => setTheme('light')}
          >
            <Sun size={16} />
            Light
          </button>
          <button
            type="button"
            className={`${styles.segmentBtn} ${theme === 'dark' ? styles.segmentActive : ''}`}
            aria-pressed={theme === 'dark'}
            onClick={() => setTheme('dark')}
          >
            <Moon size={16} />
            Dark
          </button>
        </div>
      </div>
    );
  }

  return (
    <button
      type="button"
      className={className}
      onClick={toggleTheme}
      aria-label={theme === 'dark' ? 'Switch to light theme' : 'Switch to dark theme'}
    >
      {theme === 'dark' ? <Sun size={20} /> : <Moon size={20} />}
    </button>
  );
}
