import { useState, useEffect } from 'react';
import { api } from '../api/client';
import { useDebounce } from './useDebounce';
import type { SearchResult } from '../types';

export function useSearch() {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<SearchResult[]>([]);
  const [loading, setLoading] = useState(false);
  const debouncedQuery = useDebounce(query, 300);

  useEffect(() => {
    if (!debouncedQuery.trim()) {
      setResults([]);
      return;
    }

    let cancelled = false;
    setLoading(true);

    api.search(debouncedQuery).then((data) => {
      if (!cancelled) {
        setResults(data);
        setLoading(false);
      }
    }).catch(() => {
      if (!cancelled) {
        setResults([]);
        setLoading(false);
      }
    });

    return () => { cancelled = true; };
  }, [debouncedQuery]);

  return { query, setQuery, results, loading };
}
