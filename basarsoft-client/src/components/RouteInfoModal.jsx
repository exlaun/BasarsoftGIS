import { useEffect } from 'react'
import { simulationControls, formatSimulationProgress } from '../utils/routeSimulation'
import './AttributeModal.css'
import './ShapeInfoModal.css'

const DEFAULT_ROUTE_COLOR = '#2563eb'

// Shape-style info popup for a route (Select tool on the map, or the panel's per-row info button),
// mirroring ShapeInfoModal/VehicleInfoModal. Everyone can inspect the details; admins/operators also
// get state-correct Start/Stop/Resume/End controls, and anyone can Follow a running vehicle. It reads live React
// state through props, so status/progress update while the popup stays open.
export default function RouteInfoModal({
  route,
  simulation,
  followed = false,
  canControl = false,
  busy = false,
  onSimulationAction,
  onFollow,
  onUnfollow,
  onClose,
}) {
  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  if (!route) return null

  const color = route.color || DEFAULT_ROUTE_COLOR
  const state = route.isGeometryStale ? 'Stale' : route.geometryWkt ? 'Ready' : 'Not built'
  const length = route.distanceMeters != null ? `${(route.distanceMeters / 1000).toFixed(1)} km` : '—'
  const duration = route.durationSeconds != null ? `${Math.round(route.durationSeconds / 60)} min` : '—'
  const controls = simulationControls({ simulation, canControl, followed, route })
  const started = controls.status !== 'NotStarted'

  const actionClass = (variant) =>
    'attr-modal-btn' +
    (variant === 'primary' ? ' attr-modal-save' : variant === 'danger' ? ' attr-modal-danger' : '')

  return (
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="Route info">
      <div className="attr-modal route-info-modal">
        <h2 className="attr-modal-title">Route info</h2>

        <dl className="shape-info-meta">
          <div className="shape-info-wide">
            <dt>Name</dt>
            <dd>{route.name || 'Unnamed route'}</dd>
          </div>
          <div>
            <dt>Color</dt>
            <dd className="shape-info-color">
              <span
                className="shape-info-color-swatch"
                style={{ backgroundColor: color }}
                role="img"
                aria-label="Route color"
              />
            </dd>
          </div>
          <div>
            <dt>State</dt>
            <dd>{state}</dd>
          </div>
          <div>
            <dt>Length</dt>
            <dd>{length}</dd>
          </div>
          <div>
            <dt>Duration</dt>
            <dd>{duration}</dd>
          </div>
          <div>
            <dt>Stops</dt>
            <dd>{route.stopCount ?? 0}</dd>
          </div>
          <div>
            <dt>Simulation</dt>
            <dd>
              {controls.statusLabel}
              {started ? ` · ${formatSimulationProgress(simulation?.progressPercent)}` : ''}
            </dd>
          </div>
          {started && (
            <div>
              <dt>Nearest stop</dt>
              <dd>{simulation?.currentStopName || '—'}</dd>
            </div>
          )}
        </dl>

        {(controls.actions.length > 0 || controls.showFollow) && (
          <section className="route-info-control-section" aria-label="Simulation controls">
            <span className="route-info-section-title">Simulation controls</span>
            <div className="attr-modal-actions route-info-actions">
              {controls.actions.map((action) => (
                <button
                  key={action.action}
                  type="button"
                  className={actionClass(action.variant)}
                  disabled={action.disabled || busy}
                  onClick={() => onSimulationAction(action.action, route.id)}
                >
                  {busy ? 'Working…' : action.label}
                </button>
              ))}
              {controls.showFollow && (
                <button
                  type="button"
                  className="attr-modal-btn"
                  disabled={controls.followDisabled || busy}
                  onClick={() => (controls.followAction === 'unfollow' ? onUnfollow(route.id) : onFollow(route.id))}
                >
                  {controls.followLabel}
                </button>
              )}
            </div>
          </section>
        )}
        <div className="attr-modal-actions route-info-close-row">
          <button type="button" className="attr-modal-btn attr-modal-cancel" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
