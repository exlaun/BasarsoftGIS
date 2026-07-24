import { useEffect } from 'react'
import './AttributeModal.css'
import './ShapeInfoModal.css'
import ModalCloseButton from './ModalCloseButton'

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
      <div className="attr-modal info-modal">
        <div className="attr-modal-head">
          <h2 className="attr-modal-title">Stop info</h2>
          <ModalCloseButton onClick={onClose} label="Close stop info" />
        </div>

        <dl className="shape-info-meta">
          <div className="shape-info-wide">
            <dt>Name</dt>
            <dd>{stop.name || 'Unnamed'}</dd>
          </div>
          <div>
            <dt>Route</dt>
            <dd className="poi-info-category">
              <span
                className="shape-info-color-swatch"
                style={{ background: stop.routeColor || '#2563eb' }}
                role="img"
                aria-label="Route color"
              />
              <span>{stop.routeName || '—'}</span>
            </dd>
          </div>
          {/* Only shown when this stop overrides its route's color — otherwise the swatch above
              already is its color, and a second identical swatch would just be noise. */}
          {stop.stopColor && (
            <div>
              <dt>Stop color</dt>
              <dd className="shape-info-color">
                <span
                  className="shape-info-color-swatch"
                  style={{ background: stop.stopColor }}
                  role="img"
                  aria-label="Stop color"
                />
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
        </div>
      </div>
    </div>
  )
}
