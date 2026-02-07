interface SearchBarProps {
  query: string;
  onQueryChange: (q: string) => void;
  loading: boolean;
}

export function SearchBar({ query, onQueryChange, loading }: SearchBarProps) {
  return (
    <div className="search-bar">
      <input
        type="text"
        placeholder="Search for a song..."
        value={query}
        onChange={(e) => onQueryChange(e.target.value)}
      />
      {loading && <span className="search-spinner">Searching...</span>}
      {!loading && query && (
        <button className="search-clear" onClick={() => onQueryChange('')}>
          <svg viewBox="0 0 24 24" fill="currentColor" width="16" height="16">
            <path d="M19 6.41L17.59 5 12 10.59 6.41 5 5 6.41 10.59 12 5 17.59 6.41 19 12 13.41 17.59 19 19 17.59 13.41 12z" />
          </svg>
        </button>
      )}
    </div>
  );
}
