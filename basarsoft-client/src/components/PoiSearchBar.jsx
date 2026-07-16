import { useEffect, useMemo, useRef, useState } from 'react'
import { searchPoiFeatures } from '../utils/poiSearch'
import './PoiSearchBar.css'

// Google-Maps-style search over the shared POI catalogue, floating top-left over the map. It
// filters the POI features already loaded by the map (no extra request, no
// permission gate — every authenticated role incl. Viewer sees the same catalogue), so freshly
// created/deleted POIs are in/out of the results immediately. Picking a result hands the feature
// back to MapPage, which flies the view to it and opens the read-only info panel.

const MAX_RESULTS = 8

export default function PoiSearchBar({ pois, onPick }) {
  const [query, setQuery] = useState('')
  // The query, 150 ms behind — filtering keys off this so it doesn't run per keystroke.
  const [debounced, setDebounced] = useState('')
  const [open, setOpen] = useState(false)
  const containerRef = useRef(null)

  useEffect(() => {
    const timer = window.setTimeout(() => setDebounced(query.trim()), 150)
    return () => window.clearTimeout(timer)
  }, [query])

  // `pois` is React state rather than a ref-backed getter, so a completed async load and every
  // create/delete immediately recompute an already-open query.
  const results = useMemo(
    () => searchPoiFeatures(pois, debounced, MAX_RESULTS),
    [debounced, pois],
  )

  // Click/tap anywhere outside closes the dropdown (same idiom as the modals' Escape handling).
  useEffect(() => {
    const onPointerDown = (event) => {
      if (!containerRef.current?.contains(event.target)) setOpen(false)
    }
    document.addEventListener('pointerdown', onPointerDown)
    return () => document.removeEventListener('pointerdown', onPointerDown)
  }, [])

  const pick = (feature) => {
    setOpen(false)
    setQuery('')
    onPick(feature)
  }

  const onKeyDown = (event) => {
    if (event.key === 'Enter') {
      event.preventDefault()
      if (results.length > 0) pick(results[0])
    } else if (event.key === 'Escape') {
      setQuery('')
      setOpen(false)
    }
  }

  return (
    <div className="poi-search" ref={containerRef} role="search">
      <div className="poi-search-box">
        <svg
          className="poi-search-icon"
          width="14"
          height="14"
          viewBox="0 0 24 24"
          fill="none"
          stroke="currentColor"
          strokeWidth="2"
          strokeLinecap="round"
          strokeLinejoin="round"
          aria-hidden="true"
        >
          <circle cx="11" cy="11" r="8" />
          <path d="m21 21-4.3-4.3" />
        </svg>
        <input
          type="text"
          value={query}
          onChange={(event) => {
            setQuery(event.target.value)
            setOpen(true)
          }}
          onFocus={() => query.trim() && setOpen(true)}
          onKeyDown={onKeyDown}
          placeholder="Search POIs…"
          aria-label="Search POIs by name or category"
        />
        {query && (
          <button
            type="button"
            className="poi-search-clear"
            onClick={() => {
              setQuery('')
              setOpen(false)
            }}
            aria-label="Clear search"
          >
            ×
          </button>
        )}
      </div>

      {open && debounced && (
        <ul className="poi-search-results" role="listbox" aria-label="POI search results">
          {results.length === 0 ? (
            <li className="poi-search-empty">No POI found.</li>
          ) : (
            results.map((feature) => (
              <li key={feature.get('dbId')}>
                <button type="button" className="poi-search-result" onClick={() => pick(feature)}>
                  <span
                    className="poi-search-dot"
                    style={{ backgroundColor: feature.get('color') }}
                    aria-hidden="true"
                  />
                  <span className="poi-search-texts">
                    <span className="poi-search-name">{feature.get('name')}</span>
                    <span className="poi-search-path">{feature.get('categoryPath')}</span>
                  </span>
                </button>
              </li>
            ))
          )}
        </ul>
      )}
    </div>
  )
}
