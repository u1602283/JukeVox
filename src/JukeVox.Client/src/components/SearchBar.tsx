import { Search, X, Loader2 } from 'lucide-react';
import styles from './SearchBar.module.css';

interface SearchBarProps {
  query: string;
  onQueryChange: (q: string) => void;
  loading: boolean;
}

export function SearchBar({ query, onQueryChange, loading }: SearchBarProps) {
  return (
    <div className={styles.container}>
      <span className={styles.searchIcon}>
        <Search size={18} />
      </span>
      <input
        type="text"
        placeholder="Search for a song..."
        value={query}
        onChange={(e) => onQueryChange(e.target.value)}
        className={styles.input}
        autoFocus
      />
      {loading && (
        <span className={styles.spinner}>
          <Loader2 size={18} />
        </span>
      )}
      {!loading && query && (
        <button className={styles.clearBtn} onClick={() => onQueryChange('')}>
          <X size={16} />
        </button>
      )}
    </div>
  );
}
