import { useEffect } from 'react'
import './AttributeModal.css'
import './ShapeInfoModal.css'

const DEFAULT_COLOR = '#2563eb'

// Read-only info window for a saved inventory (shape), opened by the query panel's "i" button. Unlike
// the Select-tool popup (ShapeInfoModal), this never edits — it just shows the item's attributes plus
// its stored coordinates. `info` = { id, type, name, color, createdAt, modifiedDate, modifiedUserId, wkt }.
export default function InventoryInfoModal({ info, onClose }) {
  // Escape closes the window.
  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const color = info.color || DEFAULT_COLOR
  const modifiedText = info.modifiedDate
    ? `${new Date(info.modifiedDate).toLocaleString()}${info.modifiedUserId != null ? ` · by user #${info.modifiedUserId}` : ''}`
    : null

  return (
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="Inventory info">
      <div className="attr-modal">
        <h2 className="attr-modal-title">Inventory info</h2>

        <dl className="shape-info-meta">
          <div className="shape-info-wide">
            <dt>Name</dt>
            <dd>{info.name || 'Unnamed'}</dd>
          </div>
          <div>
            <dt>Type</dt>
            <dd className="shape-info-type">{info.type}</dd>
          </div>
          <div>
            <dt>Color</dt>
            <dd className="shape-info-color">
              <span
                className="shape-info-color-swatch"
                role="img"
                aria-label="Shape color"
                style={{
                  background: color,
                }}
              />
            </dd>
          </div>
          <div>
            <dt>Created</dt>
            <dd>{new Date(info.createdAt).toLocaleString()}</dd>
          </div>
          {modifiedText && (
            <div className="shape-info-wide">
              <dt>Last edited</dt>
              <dd>{modifiedText}</dd>
            </div>
          )}
          {info.wkt && (
            <div className="shape-info-wide">
              <dt>Coordinates (WKT)</dt>
              <dd>
                <code
                  style={{
                    display: 'block',
                    maxHeight: 90,
                    overflow: 'auto',
                    fontSize: '0.72rem',
                    lineHeight: 1.4,
                    wordBreak: 'break-all',
                    opacity: 0.9,
                  }}
                >
                  {info.wkt}
                </code>
              </dd>
            </div>
          )}
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
