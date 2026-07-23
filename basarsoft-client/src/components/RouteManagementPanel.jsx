import { useLayoutEffect, useMemo, useRef, useState } from 'react'
import { clampPage, pageForItem, pageSlice, rebasePage } from '../utils/adaptivePagination'
import useAdaptivePageSize from '../utils/useAdaptivePageSize'
import { filterAndSortRoutes, routeListState } from '../utils/routeList'
import { simulationControls, simulationForRoute } from '../utils/routeSimulation'
import './RouteManagementPanel.css'

const DEFAULT_ROUTE_COLOR = '#2563eb'
// Name/State/Stops/Created share the space before the fixed info button. Fractions reset naturally
// when the page reloads, matching the Drawings panel instead of persisting a surprising old layout.
const INFO_COLUMN_PX = 32
const MIN_COLUMN_PX = 46
const STATUS_FILTERS = [
  { key: 'ready', label: 'Ready' },
  { key: 'stale', label: 'Stale' },
  { key: 'not-built', label: 'Not built' },
]

const iconProps = {
  width: 16,
  height: 16,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  'aria-hidden': true,
}

const SortChevron = ({ dir }) => (
  <svg {...iconProps} width={13} height={13} className="route-sort-chevron">
    {dir === 'asc' ? <path d="M18 15l-6-6-6 6" /> : <path d="M6 9l6 6 6-6" />}
  </svg>
)

export default function RouteManagementPanel({
  open,
  routes,
  loading,
  canManage,
  selectedRouteId,
  stops,
  stopsLoading,
  onSelectRoute,
  onNewRoute,
  onEditRoute,
  onAddStopToRoute,
  onReorderStops,
  onStopClick,
  onDeleteRoute,
  onDeleteStop,
  visibility,
  buildingRouteId,
  onToggleVisibility,
  onBuildRoute,
  canControlSimulation,
  simulationStates,
  followedRouteIds,
  simulationBusyRouteId,
  onSimulationAction,
  onFollowRoute,
  onUnfollowRoute,
  onRouteInfoClick,
  onClose,
}) {
  const [dragIndex, setDragIndex] = useState(null)
  const [overIndex, setOverIndex] = useState(null)
  const [query, setQuery] = useState('')
  const [statuses, setStatuses] = useState({
    ready: true,
    stale: true,
    'not-built': true,
  })
  const [sortBy, setSortBy] = useState('createdAt')
  const [sortDir, setSortDir] = useState('desc')
  const [page, setPage] = useState(1)
  const [columnFractions, setColumnFractions] = useState({
    name: 0.44,
    state: 0.2,
    stops: 0.13,
    created: 0.23,
  })
  const [availableColumnWidth, setAvailableColumnWidth] = useState(380)
  const listWrapRef = useRef(null)
  const tableHeaderRef = useRef(null)
  const rowRef = useRef(null)

  const filteredRoutes = useMemo(
    () => filterAndSortRoutes(routes, { query, statuses, sortBy, sortDir }),
    [routes, query, statuses, sortBy, sortDir],
  )
  const pageSize = useAdaptivePageSize({
    containerRef: listWrapRef,
    headerRef: tableHeaderRef,
    rowRef,
    fallbackRowHeight: 39,
    rowGap: 0,
    measureKey: filteredRoutes.length,
  })
  const previousPageSizeRef = useRef(pageSize)

  useLayoutEffect(() => {
    const previous = previousPageSizeRef.current
    const selectedIndex = filteredRoutes.findIndex((route) => route.id === selectedRouteId)
    setPage((current) => clampPage(
      selectedIndex >= 0
        ? pageForItem(selectedIndex, pageSize)
        : rebasePage(current, previous, pageSize),
      filteredRoutes.length,
      pageSize,
    ))
    previousPageSizeRef.current = pageSize
  }, [pageSize, filteredRoutes, selectedRouteId])

  // Measure the grid's content box so four concrete pixel widths plus the fixed info column always
  // fit both headers and rows without creating horizontal overflow.
  useLayoutEffect(() => {
    const wrapper = listWrapRef.current
    const header = tableHeaderRef.current
    if (!wrapper || !header) return undefined

    const measure = () => {
      const style = window.getComputedStyle(header)
      const horizontalPadding =
        Number.parseFloat(style.paddingLeft || '0') + Number.parseFloat(style.paddingRight || '0')
      setAvailableColumnWidth(Math.max(
        1,
        wrapper.clientWidth - horizontalPadding - INFO_COLUMN_PX,
      ))
    }

    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(wrapper)
    return () => observer.disconnect()
  }, [])

  const totalPages = Math.max(1, Math.ceil(filteredRoutes.length / pageSize))
  const visibleRoutes = pageSlice(filteredRoutes, page, pageSize)

  const changePage = (nextPage) => {
    const clamped = clampPage(nextPage, filteredRoutes.length, pageSize)
    if (clamped === page) return
    if (selectedRouteId != null) onSelectRoute(selectedRouteId)
    setPage(clamped)
  }

  const handleSort = (field) => {
    if (sortBy === field) {
      setSortDir((direction) => (direction === 'asc' ? 'desc' : 'asc'))
    } else {
      setSortBy(field)
      setSortDir(field === 'createdAt' || field === 'stopCount' ? 'desc' : 'asc')
    }
    setPage(1)
  }

  // Transfer width only between the columns on either side of the dragged divider. Their combined
  // width stays fixed, so other columns do not jump and the table remains aligned to the panel.
  const startColumnResize = (left, right) => (event) => {
    if (event.button !== 0) return
    event.preventDefault()
    event.stopPropagation()

    const startX = event.clientX
    const startLeft = columnFractions[left]
    const pairWidth = columnFractions[left] + columnFractions[right]
    const minFraction = Math.min(MIN_COLUMN_PX / availableColumnWidth, pairWidth / 2)

    const onMove = (moveEvent) => {
      const requestedLeft =
        startLeft + (moveEvent.clientX - startX) / availableColumnWidth
      const nextLeft = Math.max(
        minFraction,
        Math.min(pairWidth - minFraction, requestedLeft),
      )
      setColumnFractions((current) => ({
        ...current,
        [left]: nextLeft,
        [right]: pairWidth - nextLeft,
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

  const toggleStatus = (key) => {
    setStatuses((current) => ({ ...current, [key]: !current[key] }))
    setPage(1)
  }

  const resetDrag = () => {
    setDragIndex(null)
    setOverIndex(null)
  }

  const handleDrop = () => {
    if (dragIndex != null && overIndex != null && dragIndex !== overIndex) {
      const reordered = [...stops]
      const [moved] = reordered.splice(dragIndex, 1)
      reordered.splice(overIndex, 0, moved)
      onReorderStops(reordered.map((stop) => stop.id))
    }
    resetDrag()
  }

  const sortHeader = (field, label, className = '', resizePair = null) => (
    <div className={`route-table-heading ${className}`}>
      <button type="button" className="route-sort-btn" onClick={() => handleSort(field)}>
        {label}
        {sortBy === field && <SortChevron dir={sortDir} />}
      </button>
      {resizePair && (
        <span
          className="route-table-resize"
          onMouseDown={startColumnResize(resizePair[0], resizePair[1])}
          role="separator"
          aria-orientation="vertical"
          aria-label={`Resize ${label.toLowerCase()} column`}
        />
      )}
    </div>
  )

  const nameColumnWidth = Math.round(columnFractions.name * availableColumnWidth)
  const stateColumnWidth = Math.round(columnFractions.state * availableColumnWidth)
  const stopsColumnWidth = Math.round(columnFractions.stops * availableColumnWidth)
  // Make the final flexible column absorb rounding so the rendered pixels always add up exactly.
  const createdColumnWidth = Math.max(
    0,
    availableColumnWidth - nameColumnWidth - stateColumnWidth - stopsColumnWidth,
  )
  const routeColumnStyle = {
    '--route-name-width': `${nameColumnWidth}px`,
    '--route-state-width': `${stateColumnWidth}px`,
    '--route-stops-width': `${stopsColumnWidth}px`,
    '--route-created-width': `${createdColumnWidth}px`,
    '--route-info-width': `${INFO_COLUMN_PX}px`,
  }

  return (
    <aside
      className={`route-panel${open ? ' is-open' : ''}`}
      aria-label="Route management"
      aria-hidden={!open}
    >
      <div className="route-panel-head">
        <h2 className="route-panel-title">Route Management</h2>
        <button
          type="button"
          className="route-panel-close"
          onClick={onClose}
          aria-label="Close route management"
        >
          <svg {...iconProps}>
            <path d="M18 6L6 18M6 6l12 12" />
          </svg>
        </button>
      </div>

      {canManage && (
        <button type="button" className="route-btn route-btn-primary" onClick={onNewRoute}>
          + New route
        </button>
      )}

      <input
        type="search"
        className="route-panel-search"
        placeholder="Search by route name…"
        value={query}
        onChange={(event) => {
          setQuery(event.target.value)
          setPage(1)
        }}
      />

      <div className="route-panel-filters" aria-label="Route state filters">
        {STATUS_FILTERS.map((filter) => (
          <label key={filter.key} className="route-panel-filter">
            <input
              type="checkbox"
              checked={statuses[filter.key]}
              onChange={() => toggleStatus(filter.key)}
            />
            <span>{filter.label}</span>
          </label>
        ))}
      </div>

      <div className="route-list-wrap" ref={listWrapRef} style={routeColumnStyle}>
        <div className="route-table-header" ref={tableHeaderRef}>
          {sortHeader('name', 'Name', 'route-table-name', ['name', 'state'])}
          {sortHeader('state', 'State', '', ['state', 'stops'])}
          {sortHeader('stopCount', 'Stops', '', ['stops', 'created'])}
          {sortHeader('createdAt', 'Created')}
          <div aria-label="Info" />
        </div>

        {loading ? (
          <p className="route-panel-empty">Loading routes…</p>
        ) : routes.length === 0 ? (
          <p className="route-panel-empty">
            {canManage ? 'No routes yet. Create one to start adding stops.' : 'No routes yet.'}
          </p>
        ) : filteredRoutes.length === 0 ? (
          <p className="route-panel-empty">No routes match the current filters.</p>
        ) : (
          <ul className="route-list">
            {visibleRoutes.map((route, index) => {
              const expanded = route.id === selectedRouteId
              const routeColor = route.color || DEFAULT_ROUTE_COLOR
              const visible = visibility?.[route.id] !== false
              const routeState = routeListState(route)
              const simulation = simulationForRoute(simulationStates, route.id)
              const followed = followedRouteIds?.has(route.id) ?? false
              const simulationUi = simulationControls({
                simulation,
                canControl: canControlSimulation,
                followed,
                route,
              })
              const created = route.createdAt
                ? new Date(route.createdAt).toLocaleDateString()
                : '—'

              return (
                <li key={route.id} className="route-item">
                  <div
                    ref={index === 0 ? rowRef : undefined}
                    className={`route-row${expanded ? ' is-selected' : ''}`}
                    onClick={() => onSelectRoute(route.id)}
                    role="button"
                    tabIndex={0}
                    aria-expanded={expanded}
                    onKeyDown={(event) => {
                      if (event.key === 'Enter' || event.key === ' ') {
                        event.preventDefault()
                        onSelectRoute(route.id)
                      }
                    }}
                  >
                    <div className="route-table-name route-name-cell">
                      <span
                        className={`route-caret${expanded ? ' is-expanded' : ''}`}
                        aria-hidden="true"
                      >
                        <svg {...iconProps} width={13} height={13}>
                          <path d="M9 6l6 6-6 6" />
                        </svg>
                      </span>
                      <label
                        className="route-visibility"
                        title={visible ? 'Hide route and stops' : 'Show route and stops'}
                        onClick={(event) => event.stopPropagation()}
                        onKeyDown={(event) => event.stopPropagation()}
                      >
                        <input
                          type="checkbox"
                          checked={visible}
                          onChange={(event) => onToggleVisibility(route.id, event.target.checked)}
                          aria-label={`${visible ? 'Hide' : 'Show'} ${route.name}`}
                        />
                      </label>
                      <span
                        className="route-swatch"
                        style={{ background: routeColor }}
                        aria-hidden="true"
                      />
                      <span className="route-name" title={route.name}>
                        {route.name || 'Unnamed route'}
                      </span>
                    </div>
                    <span className={`route-state route-state-${routeState.key}`}>
                      {routeState.label}
                    </span>
                    <span className="route-stop-count">{route.stopCount ?? 0}</span>
                    <span className="route-created" title={created}>{created}</span>
                    <button
                      type="button"
                      className="route-info-btn"
                      onClick={(event) => {
                        event.stopPropagation()
                        onRouteInfoClick(route.id)
                      }}
                      onKeyDown={(event) => event.stopPropagation()}
                      title="Route info"
                      aria-label={`Info about ${route.name}`}
                    >
                      <svg {...iconProps} width={14} height={14}>
                        <circle cx="12" cy="12" r="10" />
                        <path d="M12 16v-4M12 8h.01" />
                      </svg>
                    </button>
                  </div>

                  {expanded && (
                    <div className="route-details" style={{ borderLeftColor: routeColor }}>
                      <div className="route-detail-summary">
                        {route.distanceMeters != null && (
                          <span>{(route.distanceMeters / 1000).toFixed(1)} km</span>
                        )}
                        {route.durationSeconds != null && (
                          <span>{Math.round(route.durationSeconds / 60)} min</span>
                        )}
                        {route.routingErrorCode && (
                          <span className="route-routing-error" title={route.routingErrorCode}>
                            {route.routingErrorCode}
                          </span>
                        )}
                        <span className={`route-state route-simulation-${simulationUi.status.toLowerCase()}`}>
                          Simulation: {simulationUi.statusLabel}
                        </span>
                      </div>

                      {canManage && (
                        <div className="route-detail-management">
                          <button
                            type="button"
                            className="route-btn route-stops-add"
                            onClick={() => onEditRoute(route)}
                          >
                            Edit route
                          </button>
                          <button
                            type="button"
                            className="route-btn route-stops-add is-danger"
                            onClick={() => onDeleteRoute(route)}
                          >
                            Delete route
                          </button>
                        </div>
                      )}

                      <div className="route-simulation-actions">
                        {simulationUi.actions.map((action) => (
                          <button
                            key={action.action}
                            type="button"
                            className={
                              `route-btn route-stops-add${action.variant === 'primary' ? ' is-primary' : ''}` +
                              `${action.variant === 'danger' ? ' is-danger' : ''}`
                            }
                            disabled={action.disabled || simulationBusyRouteId != null}
                            title={action.disabled
                              ? 'Build a valid, current route with at least two stops first.'
                              : undefined}
                            onClick={() => onSimulationAction(action.action, route.id)}
                          >
                            {simulationBusyRouteId === route.id ? 'Working…' : action.label}
                          </button>
                        ))}
                        {simulationUi.showFollow && (
                          <button
                            type="button"
                            className="route-btn route-stops-add"
                            disabled={simulationUi.followDisabled || simulationBusyRouteId != null}
                            onClick={() => simulationUi.followAction === 'unfollow'
                              ? onUnfollowRoute(route.id)
                              : onFollowRoute(route.id)}
                          >
                            {simulationUi.followLabel}
                          </button>
                        )}
                      </div>

                      {stopsLoading ? (
                        <p className="route-panel-hint">Loading stops…</p>
                      ) : stops.length === 0 ? (
                        <p className="route-panel-hint">
                          {canManage
                            ? 'No stops yet. Use “Add stop”, then click the map.'
                            : 'No stops yet.'}
                        </p>
                      ) : (
                        <ol className="stop-list">
                          {stops.map((stop, stopIndex) => (
                            <li
                              key={stop.id}
                              className={
                                'stop-row' +
                                (dragIndex === stopIndex ? ' is-dragging' : '') +
                                (overIndex === stopIndex && dragIndex != null && dragIndex !== stopIndex
                                  ? ' is-drop-target'
                                  : '')
                              }
                              draggable={canManage}
                              onDragStart={canManage ? () => setDragIndex(stopIndex) : undefined}
                              onDragOver={canManage
                                ? (event) => {
                                    event.preventDefault()
                                    setOverIndex(stopIndex)
                                  }
                                : undefined}
                              onDrop={canManage ? handleDrop : undefined}
                              onDragEnd={canManage ? resetDrag : undefined}
                              onClick={() => onStopClick(stop.id)}
                              role="button"
                              tabIndex={0}
                              onKeyDown={(event) => {
                                if (event.key === 'Enter' || event.key === ' ') {
                                  event.preventDefault()
                                  onStopClick(stop.id)
                                }
                              }}
                            >
                              {canManage && (
                                <span className="stop-drag-handle" aria-hidden="true">⠿</span>
                              )}
                              <span
                                className="stop-order"
                                style={{ background: stop.color || routeColor }}
                              >
                                {stop.sequenceOrder}
                              </span>
                              <span className="stop-name" title={stop.name}>
                                {stop.name || 'Unnamed'}
                              </span>
                              {canManage && (
                                <button
                                  type="button"
                                  className="route-edit-btn is-danger"
                                  onClick={(event) => {
                                    event.stopPropagation()
                                    onDeleteStop(stop)
                                  }}
                                  onKeyDown={(event) => event.stopPropagation()}
                                  title="Delete stop"
                                  aria-label={`Delete ${stop.name || 'stop'}`}
                                >
                                  Delete
                                </button>
                              )}
                            </li>
                          ))}
                        </ol>
                      )}

                      {canManage && (
                        <div className="route-stops-actions">
                          <button
                            type="button"
                            className="route-btn route-stops-add"
                            onClick={() => onAddStopToRoute(route)}
                          >
                            + Add stop
                          </button>
                          <button
                            type="button"
                            className="route-btn route-stops-add"
                            disabled={stops.length < 2 || buildingRouteId != null}
                            onClick={() => onBuildRoute(route.id)}
                          >
                            {buildingRouteId === route.id ? 'Rebuilding…' : 'Rebuild'}
                          </button>
                          {stops.length > 1 && (
                            <span className="route-panel-hint">Drag stops to reorder.</span>
                          )}
                        </div>
                      )}
                    </div>
                  )}
                </li>
              )
            })}
          </ul>
        )}
      </div>

      <div className="route-panel-foot">
        <span className="route-panel-total">
          {filteredRoutes.length === routes.length
            ? `${routes.length} ${routes.length === 1 ? 'route' : 'routes'}`
            : `${filteredRoutes.length} of ${routes.length} routes`}
        </span>
        <div className="route-panel-pager">
          <button
            type="button"
            className="route-panel-page-btn"
            disabled={page <= 1}
            onClick={() => changePage(page - 1)}
          >
            Prev
          </button>
          <span className="route-panel-page-label">{page} / {totalPages}</span>
          <button
            type="button"
            className="route-panel-page-btn"
            disabled={page >= totalPages}
            onClick={() => changePage(page + 1)}
          >
            Next
          </button>
        </div>
      </div>
    </aside>
  )
}
