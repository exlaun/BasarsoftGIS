import { useEffect, useLayoutEffect, useRef, useState } from 'react'
import { queryGeometry } from '../api/geometry'
import './QueryPanel.css'

const TYPES = ['point', 'line', 'polygon']
const DEFAULT_COLOR = '#2563eb'
const PAGE_SIZE = 10

// Trailing info-button column is a fixed width; Name/Type/Created share the rest by fraction so the
// user can drag the dividers between them. INFO_W must match the col width rendered below.
const INFO_W = 46
const MIN_COL_PX = 46 // a resizable column can't be dragged narrower than this

const iconProps = {
  width: 14,
  height: 14,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  'aria-hidden': true,
}

const SortChevron = ({ dir }) => (
  <svg {...iconProps} className="query-panel-chevron">
    {dir === 'asc' ? <path d="M18 15l-6-6-6 6" /> : <path d="M6 9l6 6 6-6" />}
  </svg>
)

// Slide-in drawer listing the user's drawings as a table. Every control re-queries the backend —
// the name filter, type filter, column sorting and paging are all resolved in SQL server-side
// (mentor requirement); this component never trims or reorders rows itself. `refreshKey` bumps
// whenever the map mutates a shape, so an open drawer stays in sync. Clicking a row hands the
// item up (MapPage zooms to the shape and opens its popup).
export default function QueryPanel({ open, refreshKey, onRowClick, onInfoClick, onClose }) {
  const [name, setName] = useState('')
  const [debouncedName, setDebouncedName] = useState('')
  const [types, setTypes] = useState({ point: true, line: true, polygon: true })
  const [sortBy, setSortBy] = useState('createdAt')
  const [sortDir, setSortDir] = useState('desc')
  const [page, setPage] = useState(1)
  const [data, setData] = useState(null)
  const [error, setError] = useState(false)

  // Resizable columns: Name/Type/Created widths as fractions of the space left after the fixed info
  // column. They always sum to 1, so the table stays exactly as wide as the panel (no overflow).
  const [colFracs, setColFracs] = useState({ name: 0.5, type: 0.22, created: 0.28 })
  // Live pixel width available to the three resizable columns (panel width minus the info column).
  // Kept in state so the <col> widths render as concrete px — `calc()` on table columns is unreliable.
  const [avail, setAvail] = useState(360)
  const wrapRef = useRef(null)

  // Debounce typing so we query once per pause, not once per keystroke.
  useEffect(() => {
    const timer = setTimeout(() => setDebouncedName(name.trim()), 300)
    return () => clearTimeout(timer)
  }, [name])

  const typesKey = TYPES.filter((t) => types[t]).join(',')

  useEffect(() => {
    if (!open || !typesKey) return undefined // closed drawer costs nothing; no types = nothing to ask
    let cancelled = false
    queryGeometry({
      name: debouncedName || undefined,
      types: typesKey,
      sortBy,
      sortDir,
      page,
      pageSize: PAGE_SIZE,
    })
      .then((result) => {
        if (cancelled) return
        setData(result)
        setError(false)
        // Deleting the last row of the last page leaves an empty page — clamp back to the new end.
        if (result.items.length === 0 && result.total > 0 && page > 1) {
          setPage(Math.max(1, Math.ceil(result.total / PAGE_SIZE)))
        }
      })
      .catch(() => {
        if (!cancelled) setError(true)
      })
    return () => {
      cancelled = true
    }
  }, [open, debouncedName, typesKey, sortBy, sortDir, page, refreshKey])

  const handleNameChange = (event) => {
    setName(event.target.value)
    setPage(1)
  }

  const toggleType = (type) => {
    setTypes((prev) => ({ ...prev, [type]: !prev[type] }))
    setPage(1)
  }

  // Click the active column to flip direction; a new column starts with its natural direction
  // (names/types read best A→Z; dates newest-first).
  const handleSort = (field) => {
    if (sortBy === field) {
      setSortDir((dir) => (dir === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortBy(field)
      setSortDir(field === 'createdAt' ? 'desc' : 'asc')
    }
    setPage(1)
  }

  // Track the table area's pixel width (measured before paint to avoid a first-frame flash) so the
  // column widths render in px and a resize drag can be expressed as a fraction of it.
  useLayoutEffect(() => {
    const el = wrapRef.current
    if (!el) return undefined
    const measure = () => setAvail(Math.max(1, el.clientWidth - INFO_W))
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(el)
    return () => observer.disconnect()
  }, [])

  // Drag the divider on the right edge of `left`, transferring width between `left` and `right` while
  // keeping their combined fraction constant (so the other columns are untouched and nothing overflows).
  const startResize = (left, right) => (event) => {
    event.preventDefault()
    event.stopPropagation()
    const startX = event.clientX
    const minFrac = MIN_COL_PX / avail
    const startLeft = colFracs[left]
    const pairSum = colFracs[left] + colFracs[right]

    const onMove = (moveEvent) => {
      let l = startLeft + (moveEvent.clientX - startX) / avail
      l = Math.max(minFrac, Math.min(pairSum - minFrac, l))
      setColFracs((prev) => ({ ...prev, [left]: l, [right]: pairSum - l }))
    }
    const onUp = () => {
      document.removeEventListener('mousemove', onMove)
      document.removeEventListener('mouseup', onUp)
      document.body.style.cursor = ''
      document.body.style.userSelect = ''
    }
    document.addEventListener('mousemove', onMove)
    document.addEventListener('mouseup', onUp)
    document.body.style.cursor = 'col-resize'
    document.body.style.userSelect = 'none'
  }

  // Width of a resizable column in px: its fraction of the space left after the fixed info column.
  const colWidth = (key) => `${Math.round(colFracs[key] * avail)}px`

  const totalPages = data ? Math.max(1, Math.ceil(data.total / PAGE_SIZE)) : 1
  const showEmpty = typesKey && data && data.total === 0
  const rows = typesKey && data ? data.items : []

  return (
    <aside className={`query-panel${open ? ' is-open' : ''}`} aria-label="Drawings list" aria-hidden={!open}>
      <div className="query-panel-head">
        <h2 className="query-panel-title">Drawings</h2>
        <button type="button" className="query-panel-close" onClick={onClose} aria-label="Close panel">
          <svg {...iconProps} width={16} height={16}>
            <path d="M18 6L6 18M6 6l12 12" />
          </svg>
        </button>
      </div>

      <input
        type="search"
        className="query-panel-search"
        placeholder="Search by name…"
        value={name}
        onChange={handleNameChange}
      />

      <div className="query-panel-types">
        {TYPES.map((type) => (
          <label key={type} className="query-panel-type">
            <input type="checkbox" checked={types[type]} onChange={() => toggleType(type)} />
            <span>{type}</span>
          </label>
        ))}
      </div>

      <div className="query-panel-table-wrap" ref={wrapRef}>
        <table className="query-panel-table">
          <colgroup>
            <col style={{ width: colWidth('name') }} />
            <col style={{ width: colWidth('type') }} />
            <col style={{ width: colWidth('created') }} />
            <col style={{ width: `${INFO_W}px` }} />
          </colgroup>
          <thead>
            <tr>
              <th>
                <button type="button" className="query-panel-sort" onClick={() => handleSort('name')}>
                  Name {sortBy === 'name' && <SortChevron dir={sortDir} />}
                </button>
                <span
                  className="query-panel-resize"
                  onMouseDown={startResize('name', 'type')}
                  role="separator"
                  aria-label="Resize name column"
                />
              </th>
              <th>
                <button type="button" className="query-panel-sort" onClick={() => handleSort('type')}>
                  Type {sortBy === 'type' && <SortChevron dir={sortDir} />}
                </button>
                <span
                  className="query-panel-resize"
                  onMouseDown={startResize('type', 'created')}
                  role="separator"
                  aria-label="Resize type column"
                />
              </th>
              <th>
                <button type="button" className="query-panel-sort" onClick={() => handleSort('createdAt')}>
                  Created {sortBy === 'createdAt' && <SortChevron dir={sortDir} />}
                </button>
              </th>
              <th className="query-panel-info-th" aria-label="Info" />
            </tr>
          </thead>
          <tbody>
            {rows.map((item) => (
              <tr
                key={`${item.type}-${item.id}`}
                className="query-panel-row"
                onClick={() => onRowClick(item)}
                title={`Last edited ${new Date(item.modifiedDate).toLocaleString()} — click to show on map`}
              >
                <td>
                  <span
                    className="query-panel-swatch"
                    style={{ background: item.color || DEFAULT_COLOR }}
                    aria-hidden="true"
                  />
                  {item.name ?? 'Unnamed'}
                </td>
                <td className="query-panel-type-cell">{item.type}</td>
                <td className="query-panel-date">{new Date(item.createdAt).toLocaleDateString()}</td>
                <td className="query-panel-info-cell">
                  <button
                    type="button"
                    className="query-panel-info-btn"
                    title="Show inventory info"
                    aria-label={`Info about ${item.name ?? 'shape'}`}
                    onClick={(event) => {
                      event.stopPropagation() // don't trigger the row's zoom+edit click
                      onInfoClick(item)
                    }}
                  >
                    <svg {...iconProps}>
                      <circle cx="12" cy="12" r="10" />
                      <path d="M12 16v-4M12 8h.01" />
                    </svg>
                  </button>
                </td>
              </tr>
            ))}
          </tbody>
        </table>

        {!typesKey && <p className="query-panel-empty">Select at least one type to list drawings.</p>}
        {showEmpty && <p className="query-panel-empty">No drawings match.</p>}
        {error && <p className="query-panel-empty query-panel-error">Could not load drawings.</p>}
      </div>

      <div className="query-panel-foot">
        <span className="query-panel-total">
          {typesKey && data ? `${data.total} ${data.total === 1 ? 'drawing' : 'drawings'}` : ''}
        </span>
        <div className="query-panel-pager">
          <button
            type="button"
            className="query-panel-page-btn"
            onClick={() => setPage((p) => Math.max(1, p - 1))}
            disabled={page <= 1}
          >
            Prev
          </button>
          <span className="query-panel-page-label">
            {page} / {totalPages}
          </span>
          <button
            type="button"
            className="query-panel-page-btn"
            onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
            disabled={page >= totalPages}
          >
            Next
          </button>
        </div>
      </div>
    </aside>
  )
}
