import { useEffect } from 'react'
import './AttributeModal.css'
import './ShapeInfoModal.css'

// Read-only info popup for a stop on the map (clicked with the Select tool), following the existing
// GIS point-popup pattern (PoiInfoModal). Authorized transport managers can start an explicit map
// relocation; all metadata remains read-only here.
// stopColor is the stop's own override or null when it inherits routeColor.
export default function StopInfoModal({ stop, canRelocate = false, onRelocate, onClose }) {
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
          {/* Only shown when this stop overrides its route's color — otherwise the swatch above
              already is its color, and a second identical swatch would just be noise. */}
          {stop.stopColor && (
            <div>
              <dt>Stop color</dt>
              <dd className="poi-info-category">
                <span
                  aria-hidden="true"
                  style={{
                    display: 'inline-block',
                    width: 12,
                    height: 12,
                    borderRadius: 3,
                    background: stop.stopColor,
                    border: '1px solid rgba(0, 0, 0, 0.25)',
                  }}
                />
                <span>{stop.stopColor}</span>
              </dd>
            </div>
          )}
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
          {canRelocate && (
            <button
              type="button"
              className="attr-modal-btn shape-info-edit-location"
              onClick={onRelocate}
            >
              Relocate
            </button>
          )}
          <button type="button" className="attr-modal-btn attr-modal-cancel" onClick={onClose}>
            Close
          </button>
        </div>
      </div>
    </div>
  )
}
