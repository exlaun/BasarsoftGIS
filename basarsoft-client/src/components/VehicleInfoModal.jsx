import { useEffect } from 'react'
import { formatSimulationProgress } from '../utils/routeSimulation'
import './AttributeModal.css'
import './ShapeInfoModal.css'

export default function VehicleInfoModal({ route, simulation, onClose }) {
  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const position = simulation.longitude != null && simulation.latitude != null
    ? `${simulation.longitude.toFixed(5)}, ${simulation.latitude.toFixed(5)}`
    : '—'

  return (
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="Vehicle info">
      <div className="attr-modal">
        <h2 className="attr-modal-title">Vehicle info</h2>
        <dl className="shape-info-meta">
          <div className="shape-info-wide"><dt>Route</dt><dd>{route?.name ?? `Route ${simulation.routeId}`}</dd></div>
          <div><dt>Status</dt><dd>{simulation.status}</dd></div>
          <div><dt>Progress</dt><dd>{formatSimulationProgress(simulation.progressPercent)}</dd></div>
          <div><dt>Nearest stop</dt><dd>{simulation.currentStopName || '—'}</dd></div>
          <div className="shape-info-wide"><dt>Position</dt><dd>{position}</dd></div>
        </dl>
        <div className="attr-modal-actions">
          <button type="button" className="attr-modal-btn attr-modal-cancel" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
