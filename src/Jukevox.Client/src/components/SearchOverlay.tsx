import { useEffect, useRef, useCallback } from 'react';
import { X } from 'lucide-react';
import { SearchBar } from './SearchBar';
import { SearchResults } from './SearchResults';
import type { SearchResult } from '../types';
import styles from './SearchOverlay.module.css';

interface SearchOverlayProps {
  open: boolean;
  onClose: () => void;
  query: string;
  onQueryChange: (q: string) => void;
  results: SearchResult[];
  loading: boolean;
}

export function SearchOverlay({ open, onClose, query, onQueryChange, results, loading }: SearchOverlayProps) {
  const panelRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement | null>(null);

  const handleKeyDown = useCallback((e: KeyboardEvent) => {
    if (e.key === 'Escape') onClose();
  }, [onClose]);

  useEffect(() => {
    if (!open) return;
    document.addEventListener('keydown', handleKeyDown);
    // Auto-focus the search input
    requestAnimationFrame(() => {
      const input = panelRef.current?.querySelector<HTMLInputElement>('input[type="text"]');
      if (input) {
        inputRef.current = input;
        input.focus();
      }
    });
    return () => document.removeEventListener('keydown', handleKeyDown);
  }, [open, handleKeyDown]);

  if (!open) return null;

  return (
    <>
      <div className={styles.scrim} onClick={onClose} />
      <div className={styles.panel} ref={panelRef}>
        <div className={styles.panelHeader}>
          <SearchBar query={query} onQueryChange={onQueryChange} loading={loading} />
          <button className={styles.closeBtn} onClick={onClose} aria-label="Close search">
            <X size={20} />
          </button>
        </div>
        <div className={styles.panelResults}>
          <SearchResults results={results} />
        </div>
      </div>
    </>
  );
}
