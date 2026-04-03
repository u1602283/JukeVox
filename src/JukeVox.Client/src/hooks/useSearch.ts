import { useState, useEffect } from 'react';
import { api } from '../api/client';
import { useDebounce } from './useDebounce';
import type { SearchResult } from '../types';

export function useSearch() {
  const [query, setQuery] = useState('');
  const [resultState, setResultState] = useState<{ query: string; results: SearchResult[] }>({ query: '', results: [] });
  const debouncedQuery = useDebounce(query, 300);

  useEffect(() => {
    if (!debouncedQuery.trim()) return;

    let cancelled = false;

    api.search(debouncedQuery).then((data) => {
      if (!cancelled) {
        setResultState({ query: debouncedQuery, results: data });
      }
    }).catch(() => {
      if (!cancelled) {
        setResultState({ query: debouncedQuery, results: [] });
      }
    });

    return () => { cancelled = true; };
  }, [debouncedQuery]);

  const trimmed = debouncedQuery.trim();
  const loading = trimmed !== '' && resultState.query !== debouncedQuery;
  const results = trimmed ? resultState.results : [];
  return { query, setQuery, results, loading };
}
