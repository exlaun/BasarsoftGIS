import { useMemo } from 'react'
import { simulationControls, formatSimulationProgress } from '../utils/routeSimulation'
import { simulationProgressMetrics, simulationStopContext } from '../utils/simulationProgress'
import './SimulationProgressCard.css'

const DEFAULT_ROUTE_COLOR = '#2563eb'

const formatDistance = (meters) => {
  if (!Number.isFinite(meters)) return '—'
  if (meters < 1000) return `${Math.round(meters)} m`
  return `${(meters / 1000).toFixed(1)} km`
}

const formatDuration = (seconds) => {
  if (!Number.isFinite(Number(seconds))) return '—'
  const minutes = Math.max(0, Math.round(Number(seconds) / 60))
  if (minutes < 60) return `${minutes} min`
  const hours = Math.floor(minutes / 60)
  const remainder = minutes % 60
  return remainder ? `${hours} h ${remainder} min` : `${hours} h`
}

function StopContextEntry({ entry }) {
  const moving = entry.type === 'moving'
  const stopped = entry.type === 'stopped'
  const vehicleState = moving || stopped
  return (
    <li className={`simulation-stop-entry is-${entry.type}${vehicleState ? ' is-vehicle-state' : ''}`}>
      <span className="simulation-stop-marker" aria-hidden="true">
        {vehicleState ? '●' : entry.stop?.sequenceOrder}
      </span>
      <span className="simulation-stop-copy">
        <span className="simulation-stop-label">{entry.label}</span>
        <span className="simulation-stop-name">
          {moving ? 'Vehicle is moving' : stopped ? 'Vehicle is stopped' : entry.stop?.name || 'Unnamed stop'}
        </span>
      </span>
      {vehicleState && (
        <img
          className="simulation-car-glyph"
          src={`${import.meta.env.BASE_URL}vehicle-icon.svg`}
          alt={moving ? 'Vehicle moving' : 'Vehicle stopped'}
        />
      )}
    </li>
  )
}

export default function SimulationProgressCard({
  route,
  simulation,
  stops = [],
  stopsLoading = false,
  stopsError = false,
  collapsed = false,
  sidePanelOpen = false,
  canControl = false,
  busy = false,
  onToggleCollapsed,
  onSimulationAction,
  onUnfollow,
}) {
  const context = useMemo(
    () => simulationStopContext(stops, simulation),
    [simulation, stops],
  )
  const metrics = useMemo(
    () => simulationProgressMetrics(route, simulation),
    [route, simulation],
  )
  const controls = simulationControls({
    simulation,
    canControl,
    cameraFollowed: true,
    route,
  })

  if (!route || !simulation || simulation.status === 'NotStarted') return null

  const routeColor = route.color || DEFAULT_ROUTE_COLOR
  const detailStop = context.nearestStop?.name || simulation.currentStopName || '—'
  const detailStopLabel = 'Nearest stop'

  if (collapsed) {
    return (
      <aside className={`simulation-progress-card is-collapsed${sidePanelOpen ? ' is-side-panel-open' : ''}`} aria-label="Simulation progress">
        <button
          type="button"
          className="simulation-card-collapsed-toggle"
          onClick={onToggleCollapsed}
          aria-expanded="false"
          aria-label="Expand simulation progress"
        >
          <img
            className="simulation-card-mini-car"
            src={`${import.meta.env.BASE_URL}vehicle-icon.svg`}
            alt=""
          />
          <span className="simulation-card-collapsed-copy">
            <strong>{route.name || `Route ${route.id}`}</strong>
            <span>{controls.statusLabel} · {formatSimulationProgress(metrics.completedPercent)}</span>
          </span>
          <span aria-hidden="true">⌄</span>
        </button>
      </aside>
    )
  }

  return (
    <aside className={`simulation-progress-card${sidePanelOpen ? ' is-side-panel-open' : ''}`} aria-label="Simulation progress">
      <header className="simulation-card-header">
        <div className="simulation-card-title-wrap">
          <span className="simulation-card-route-dot" style={{ backgroundColor: routeColor }} aria-hidden="true" />
          <div>
            <p className="simulation-card-eyebrow">Live simulation</p>
            <h2>{route.name || `Route ${route.id}`}</h2>
          </div>
        </div>
        <button
          type="button"
          className="simulation-card-icon-button"
          onClick={onToggleCollapsed}
          aria-expanded="true"
          aria-label="Collapse simulation progress"
        >
          −
        </button>
      </header>

      <div className="simulation-card-status-row">
        <span className={`simulation-card-status is-${String(controls.status).toLowerCase()}`}>
          {controls.statusLabel}
        </span>
        <span className="simulation-card-completion">
          {formatSimulationProgress(metrics.completedPercent)}
        </span>
      </div>

      <div className="simulation-progress-track" role="progressbar" aria-valuemin="0" aria-valuemax="100" aria-valuenow={metrics.completedPercent} aria-label="Route completion">
        <span style={{ width: `${metrics.completedPercent}%` }} />
      </div>
      <section className="simulation-stop-section" aria-label="Route stops">
        <div className="simulation-card-section-heading">
          <span>Stop sequence</span>
          <span>{context.orderedStops.length || route.stopCount || 0} stops</span>
        </div>
        {stopsLoading ? (
          <p className="simulation-card-message">Loading stops…</p>
        ) : stopsError ? (
          <p className="simulation-card-message is-error">Could not load route stops.</p>
        ) : context.entries.length === 0 ? (
          <p className="simulation-card-message">No stop details available.</p>
        ) : (
          <ol className="simulation-stop-list">
            {context.entries.map((entry) => <StopContextEntry key={entry.key} entry={entry} />)}
          </ol>
        )}
      </section>

      <section className="simulation-card-info" aria-label="Simulation details">
        <div><span>{detailStopLabel}</span><strong>{detailStop}</strong></div>
        <div><span>Planned duration</span><strong>{formatDuration(route.durationSeconds)}</strong></div>
        <div><span>Distance remaining</span><strong>{formatDistance(metrics.remainingDistanceMeters)}</strong></div>
        <div><span>Total distance</span><strong>{formatDistance(route.distanceMeters)}</strong></div>
      </section>

      {(controls.actions.length > 0 || controls.showFollow) && (
        <div className={`simulation-card-actions${controls.actions.length === 1 && controls.actions[0].action === 'end' ? ' is-terminal' : ''}`} aria-label="Simulation controls">
          {controls.actions.map((action) => (
            <button
              key={action.action}
              type="button"
              className={`simulation-card-action is-${action.action}${action.variant === 'danger' ? ' is-danger' : action.variant === 'primary' ? ' is-primary' : ''}`}
              disabled={action.disabled || busy}
              onClick={() => onSimulationAction(action.action, route.id)}
            >
              {busy ? 'Working…' : action.label}
            </button>
          ))}
          {controls.showFollow && (
            <button
              type="button"
              className="simulation-card-action is-follow"
              disabled={busy}
              onClick={() => onUnfollow(route.id)}
            >
              Stop Following
            </button>
          )}
        </div>
      )}
    </aside>
  )
}
