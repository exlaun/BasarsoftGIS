import { useLayoutEffect, useRef, useState } from 'react'
import { clampPage, pageForItem, pageSlice, rebasePage } from '../utils/adaptivePagination'
import useAdaptivePageSize from '../utils/useAdaptivePageSize'
import './RouteManagementPanel.css'

// Fallback swatch/marker color for a route with no color set. Kept in sync with STOP_DEFAULT_COLOR in
// MapPage (the map marker style) so a route looks the same in the panel and on the map.
const DEFAULT_ROUTE_COLOR = '#2563eb'

// Same inline-SVG convention as QueryPanel/DrawToolbar — no icon dependency, inherits currentColor.
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

// Right-side slide-in drawer for browsing and managing transportation routes and their stops, sharing
// the right edge (and its geometry) with QueryPanel — MapPage slides one out as the other opens.
// Presentational about the map: the routes/stops data, selection, and every API call live in MapPage;
// this renders the lists and raises intent through props, owning only the drag-in-progress UI state.
// Visible to every authenticated user; the write controls (New / Edit / Delete / Add stop /
// drag-reorder) render only when canManage (the manage_transport permission) is true, so End Users get
// a read-only browser. Deletion is confirmed in MapPage, not here — this only raises the intent.
//
// The list is an accordion: selecting a route expands it in place and its stops render nested
// directly beneath that route's own row, indented behind a spine in the route's color. Only the
// selected route is ever expanded, so `stops` (which MapPage loads for the selection) always belongs
// to the one open route.
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
  onClose,
}) {
  // Native HTML5 drag: index of the row being dragged and the row it's currently over.
  const [dragIndex, setDragIndex] = useState(null)
  const [overIndex, setOverIndex] = useState(null)
  const [page, setPage] = useState(1)
  const listWrapRef = useRef(null)
  const rowRef = useRef(null)
  const pageSize = useAdaptivePageSize({
    containerRef: listWrapRef,
    rowRef,
    fallbackRowHeight: 38,
    rowGap: 5,
    measureKey: routes.length,
  })
  const previousPageSizeRef = useRef(pageSize)

  useLayoutEffect(() => {
    const previous = previousPageSizeRef.current
    const selectedIndex = routes.findIndex((route) => route.id === selectedRouteId)
    setPage((current) => clampPage(
      selectedIndex >= 0
        ? pageForItem(selectedIndex, pageSize)
        : rebasePage(current, previous, pageSize),
      routes.length,
      pageSize,
    ))
    previousPageSizeRef.current = pageSize
  }, [pageSize, routes, selectedRouteId])

  const totalPages = Math.max(1, Math.ceil(routes.length / pageSize))
  const visibleRoutes = pageSlice(routes, page, pageSize)

  const changePage = (nextPage) => {
    const clamped = clampPage(nextPage, routes.length, pageSize)
    if (clamped === page) return
    if (selectedRouteId != null) onSelectRoute(selectedRouteId)
    setPage(clamped)
  }

  const resetDrag = () => {
    setDragIndex(null)
    setOverIndex(null)
  }

  // On drop, splice the dragged stop into its new slot and hand the new id order up to persist.
  const handleDrop = () => {
    if (dragIndex != null && overIndex != null && dragIndex !== overIndex) {
      const reordered = [...stops]
      const [moved] = reordered.splice(dragIndex, 1)
      reordered.splice(overIndex, 0, moved)
      onReorderStops(reordered.map((stop) => stop.id))
    }
    resetDrag()
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

      <div className="route-list-wrap" ref={listWrapRef}>
        {loading ? (
          <p className="route-panel-hint">Loading routes…</p>
        ) : routes.length === 0 ? (
          <p className="route-panel-hint">
            {canManage ? 'No routes yet. Create one to start adding stops.' : 'No routes yet.'}
          </p>
        ) : (
          <ul className="route-list">
            {visibleRoutes.map((route, index) => {
              const expanded = route.id === selectedRouteId
              const routeColor = route.color || DEFAULT_ROUTE_COLOR
              const visible = visibility?.[route.id] !== false
              const routeState = route.isGeometryStale
                ? 'Stale'
                : route.geometryWkt
                  ? 'Ready'
                  : 'Not built'
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
                    <span
                      className={`route-caret${expanded ? ' is-expanded' : ''}`}
                      aria-hidden="true"
                    >
                      <svg {...iconProps} width={14} height={14}>
                        <path d="M9 6l6 6-6 6" />
                      </svg>
                    </span>
                    <span
                      className="route-swatch"
                      style={{ background: routeColor }}
                      aria-hidden="true"
                    />
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
                    <span className="route-name" title={route.name}>
                      {route.name}
                    </span>
                    <span className="route-stop-count">
                      {route.stopCount} {route.stopCount === 1 ? 'stop' : 'stops'}
                    </span>
                    {/* The row itself is the route's select target, and it answers Enter/Space as well
                        as clicks. So every control sitting on it has to stop BOTH — a bare onClick
                        guard still lets a keyboard activation bubble up and expand the row too. Same
                        pattern as the visibility checkbox above. */}
                    {canManage && (
                      <>
                        <button
                          type="button"
                          className="route-edit-btn"
                          onClick={(event) => {
                            event.stopPropagation()
                            onEditRoute(route)
                          }}
                          onKeyDown={(event) => event.stopPropagation()}
                          title="Edit route"
                        >
                          Edit
                        </button>
                        <button
                          type="button"
                          className="route-edit-btn is-danger"
                          onClick={(event) => {
                            event.stopPropagation()
                            onDeleteRoute(route)
                          }}
                          onKeyDown={(event) => event.stopPropagation()}
                          title="Delete route and its stops"
                        >
                          Delete
                        </button>
                      </>
                    )}
                  </div>

                  {/* Nested under the route's own row: the spine takes the route's color, so a stop
                      list is visually tied to the route it belongs to. */}
                  {expanded && (
                    <div className="route-stops" style={{ borderLeftColor: routeColor }}>
                      <div className="route-build-summary">
                        <span className={`route-state route-state-${routeState.toLowerCase().replace(' ', '-')}`}>
                          {routeState}
                        </span>
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
                          {stops.map((stop, index) => (
                            <li
                              key={stop.id}
                              className={
                                'stop-row' +
                                (dragIndex === index ? ' is-dragging' : '') +
                                (overIndex === index && dragIndex != null && dragIndex !== index
                                  ? ' is-drop-target'
                                  : '')
                              }
                              draggable={canManage}
                              onDragStart={canManage ? () => setDragIndex(index) : undefined}
                              onDragOver={
                                canManage
                                  ? (event) => {
                                      event.preventDefault()
                                      setOverIndex(index)
                                    }
                                  : undefined
                              }
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
                                <span className="stop-drag-handle" aria-hidden="true">
                                  ⠿
                                </span>
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
          {routes.length} {routes.length === 1 ? 'route' : 'routes'}
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
