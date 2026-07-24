import { useLayoutEffect, useMemo, useRef, useState } from 'react'
import { sortAdminRows } from '../utils/adminTableModel'
import './AdminTable.css'

const DEFAULT_MIN_WIDTH = 72
const DEFAULT_FLEX = 1

const iconProps = {
  width: 13,
  height: 13,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  'aria-hidden': true,
}

const SortChevron = ({ direction }) => (
  <svg {...iconProps}>
    {direction === 'asc' ? <path d="M18 15l-6-6-6 6" /> : <path d="M6 9l6 6 6-6" />}
  </svg>
)

function initialFractions(columns) {
  const flexible = columns.filter((column) => column.fixedWidth == null)
  const total = flexible.reduce((sum, column) => sum + (column.flex ?? DEFAULT_FLEX), 0) || 1
  return Object.fromEntries(flexible.map((column) => [column.key, (column.flex ?? DEFAULT_FLEX) / total]))
}

function allocateWidths(columns, fractions, totalWidth) {
  const flexible = columns.filter((column) => column.fixedWidth == null)
  const widths = new Map()
  let remainingWidth = totalWidth
  let remainingFraction = flexible.reduce(
    (sum, column) => sum + (fractions[column.key] ?? 0),
    0,
  ) || 1
  const pending = [...flexible]

  while (pending.length > 0) {
    const column = pending.shift()
    const fraction = fractions[column.key] ?? 0
    const proposed = remainingWidth * (fraction / remainingFraction)
    const minWidth = column.minWidth ?? DEFAULT_MIN_WIDTH
    if (proposed < minWidth && pending.length > 0) {
      widths.set(column.key, minWidth)
      remainingWidth -= minWidth
      remainingFraction -= fraction
    } else {
      widths.set(column.key, Math.max(minWidth, proposed))
    }
  }

  return widths
}

export default function AdminTable({
  columns,
  rows,
  getRowKey,
  defaultSortKey = null,
  defaultSortDir = 'asc',
  sortRows = sortAdminRows,
  onRowClick,
  selectedRowKey = null,
}) {
  const wrapRef = useRef(null)
  const [sortKey, setSortKey] = useState(defaultSortKey)
  const [sortDir, setSortDir] = useState(defaultSortDir)
  const [columnFractions, setColumnFractions] = useState(() => initialFractions(columns))
  const [availableWidth, setAvailableWidth] = useState(1)

  const fixedWidth = useMemo(
    () => columns.reduce((sum, column) => sum + (column.fixedWidth ?? 0), 0),
    [columns],
  )
  const minimumFlexibleWidth = useMemo(
    () => columns
      .filter((column) => column.fixedWidth == null)
      .reduce((sum, column) => sum + (column.minWidth ?? DEFAULT_MIN_WIDTH), 0),
    [columns],
  )

  useLayoutEffect(() => {
    const wrapper = wrapRef.current
    if (!wrapper) return undefined
    const measure = () => setAvailableWidth(Math.max(1, wrapper.clientWidth - fixedWidth))
    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(wrapper)
    return () => observer.disconnect()
  }, [fixedWidth])

  const flexibleWidth = Math.max(minimumFlexibleWidth, availableWidth)
  const columnWidths = useMemo(
    () => allocateWidths(columns, columnFractions, flexibleWidth),
    [columns, columnFractions, flexibleWidth],
  )
  const sortedRows = useMemo(
    () => sortRows(rows, columns, sortKey, sortDir),
    [rows, columns, sortKey, sortDir, sortRows],
  )

  const handleSort = (column) => {
    if (column.sortable === false) return
    if (sortKey === column.key) {
      setSortDir((direction) => (direction === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortKey(column.key)
      setSortDir(column.defaultSortDir ?? 'asc')
    }
  }

  const startResize = (leftKey, rightKey) => (event) => {
    if (event.button !== 0) return
    event.preventDefault()
    event.stopPropagation()

    const startX = event.clientX
    const startLeft = columnFractions[leftKey]
    const pairFraction = startLeft + columnFractions[rightKey]
    const minFraction = Math.min(
      (columns.find((column) => column.key === leftKey)?.minWidth ?? DEFAULT_MIN_WIDTH) / flexibleWidth,
      pairFraction / 2,
    )

    const onMove = (moveEvent) => {
      const requested = startLeft + (moveEvent.clientX - startX) / flexibleWidth
      const nextLeft = Math.max(minFraction, Math.min(pairFraction - minFraction, requested))
      setColumnFractions((current) => ({
        ...current,
        [leftKey]: nextLeft,
        [rightKey]: pairFraction - nextLeft,
      }))
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

  const getResizePair = (index) => {
    const column = columns[index]
    const next = columns[index + 1]
    if (column.fixedWidth != null || column.resizable === false || next?.fixedWidth != null) return null
    if (!next) return null
    return [column.key, next.key]
  }

  return (
    <div className="admin-table-wrap" ref={wrapRef}>
      <table
        className="admin-table admin-table-interactive"
        style={{ width: `${flexibleWidth + fixedWidth}px`, minWidth: `${flexibleWidth + fixedWidth}px` }}
      >
        <colgroup>
          {columns.map((column) => (
            <col
              key={column.key}
              style={{
                width: column.fixedWidth != null
                  ? `${column.fixedWidth}px`
                  : `${columnWidths.get(column.key)}px`,
              }}
            />
          ))}
        </colgroup>
        <thead>
          <tr>
            {columns.map((column, index) => {
              const resizePair = getResizePair(index)
              const active = sortKey === column.key
              const sortable = column.sortable !== false
              return (
                <th
                  key={column.key}
                  className={column.headerClassName}
                  style={{ textAlign: column.align ?? 'left' }}
                  aria-sort={active ? (sortDir === 'asc' ? 'ascending' : 'descending') : undefined}
                >
                  {sortable ? (
                    <button type="button" className="admin-table-sort" onClick={() => handleSort(column)}>
                      {column.label}
                      {active && <SortChevron direction={sortDir} />}
                    </button>
                  ) : column.label}
                  {resizePair && (
                    <span
                      className="admin-table-resize"
                      onMouseDown={startResize(resizePair[0], resizePair[1])}
                      role="separator"
                      aria-orientation="vertical"
                      aria-label={`Resize ${column.label.toLowerCase()} column`}
                    />
                  )}
                </th>
              )
            })}
          </tr>
        </thead>
        <tbody>
          {sortedRows.map((row) => {
            const rowKey = getRowKey(row)
            const selected = selectedRowKey === rowKey
            const clickable = typeof onRowClick === 'function'
            const shouldIgnoreRowInteraction = (event) => event.target?.closest?.(
              'button, a, input, select, textarea',
            )

            return (
            <tr
              key={rowKey}
              className={`${clickable ? 'admin-table-row-clickable' : ''}${selected ? ' is-selected' : ''}`}
              onClick={clickable
                ? (event) => {
                  if (!shouldIgnoreRowInteraction(event)) onRowClick(row)
                }
                : undefined}
              onKeyDown={clickable
                ? (event) => {
                  if ((event.key === 'Enter' || event.key === ' ') && !shouldIgnoreRowInteraction(event)) {
                    event.preventDefault()
                    onRowClick(row)
                  }
                }
                : undefined}
              role={clickable ? 'button' : undefined}
              tabIndex={clickable ? 0 : undefined}
              aria-expanded={clickable ? selected : undefined}
            >
              {columns.map((column) => {
                const cellClassName = typeof column.cellClassName === 'function'
                  ? column.cellClassName(row)
                  : column.cellClassName
                return (
                  <td
                    key={column.key}
                    className={cellClassName}
                    style={{ textAlign: column.align ?? 'left' }}
                  >
                    {column.render ? column.render(row) : row[column.key]}
                  </td>
                )
              })}
            </tr>
            )
          })}
        </tbody>
      </table>
    </div>
  )
}
