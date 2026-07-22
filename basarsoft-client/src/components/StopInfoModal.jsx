import { useEffect } from 'react'
import './AttributeModal.css'
import './ShapeInfoModal.css'

// Read-only info popup for a stop on the map (clicked with the Select tool), following the existing
// GIS point-popup pattern (PoiInfoModal). Stops have no edit or delete in this module, so it is
// view-only. `stop` = { name, routeName, routeColor, sequenceOrder, createdBy, createdAt }.
export default function StopInfoModal({ stop, onClose }) {
  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  return (
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="Stop info">
      <div className="attr-modal">
        <h2 className="attr-modal-title">Stop info</h2>

        <dl className="shape-info-meta">
          <div>
            <dt>Name</dt>
            <dd>{stop.name || 'Unnamed'}</dd>
          </div>
          <div>
            <dt>Route</dt>
            <dd className="poi-info-category">
              <span
                aria-hidden="true"
                style={{
                  display: 'inline-block',
                  width: 12,
                  height: 12,
                  borderRadius: 3,
                  background: stop.routeColor || '#2563eb',
                  border: '1px solid rgba(0, 0, 0, 0.25)',
                }}
              />
              <span>{stop.routeName || '—'}</span>
            </dd>
          </div>
          <div>
            <dt>Stop order</dt>
            <dd>#{stop.sequenceOrder}</dd>
          </div>
          <div>
            <dt>Added by</dt>
            <dd>{stop.createdBy || '—'}</dd>
          </div>
          <div>
            <dt>Added on</dt>
            <dd>{stop.createdAt ? new Date(stop.createdAt).toLocaleString() : '—'}</dd>
          </div>
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
