import { useState } from 'react'
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
// Visible to every authenticated user; the write controls (New / Edit / Add stop / drag-reorder)
// render only when canManage (the manage_transport permission) is true, so End Users get a read-only
// browser.
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
  onClose,
}) {
  // Native HTML5 drag: index of the row being dragged and the row it's currently over.
  const [dragIndex, setDragIndex] = useState(null)
  const [overIndex, setOverIndex] = useState(null)

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

      <div className="route-list-wrap">
        {loading ? (
          <p className="route-panel-hint">Loading routes…</p>
        ) : routes.length === 0 ? (
          <p className="route-panel-hint">
            {canManage ? 'No routes yet. Create one to start adding stops.' : 'No routes yet.'}
          </p>
        ) : (
          <ul className="route-list">
            {routes.map((route) => {
              const expanded = route.id === selectedRouteId
              const routeColor = route.color || DEFAULT_ROUTE_COLOR
              return (
                <li key={route.id} className="route-item">
                  <div
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
                    <span className="route-name" title={route.name}>
                      {route.name}
                    </span>
                    <span className="route-stop-count">
                      {route.stopCount} {route.stopCount === 1 ? 'stop' : 'stops'}
                    </span>
                    {canManage && (
                      <button
                        type="button"
                        className="route-edit-btn"
                        onClick={(event) => {
                          event.stopPropagation()
                          onEditRoute(route)
                        }}
                        title="Edit route"
                      >
                        Edit
                      </button>
                    )}
                  </div>

                  {/* Nested under the route's own row: the spine takes the route's color, so a stop
                      list is visually tied to the route it belongs to. */}
                  {expanded && (
                    <div className="route-stops" style={{ borderLeftColor: routeColor }}>
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
                              <span className="stop-order" style={{ background: routeColor }}>
                                {stop.sequenceOrder}
                              </span>
                              <span className="stop-name" title={stop.name}>
                                {stop.name || 'Unnamed'}
                              </span>
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
    </aside>
  )
}
