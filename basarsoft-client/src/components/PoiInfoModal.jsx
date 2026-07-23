import { useEffect } from 'react'
import './AttributeModal.css'
import './ShapeInfoModal.css'
import { formatTime } from '../utils/poiCategories'
import PoiIconBadge from './PoiIconBadge'

// Read-only info panel for a POI on the map (Select tool click), the POI counterpart of the shape
// info popup. POIs are shared data, so there is no editing here — just the details plus a Delete
// button when the caller has manage_pois. `poi` = { name, categoryPath, openTime,
// closeTime, createdBy, createdAt, categoryColor, categoryIconKey }.
export default function PoiInfoModal({ poi, canDelete, onDelete, onClose }) {
  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  return (
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="POI info">
      <div className="attr-modal">
        <h2 className="attr-modal-title">POI info</h2>

        <dl className="shape-info-meta">
          <div className="shape-info-wide">
            <dt>Name</dt>
            <dd>{poi.name || 'Unnamed'}</dd>
          </div>
          <div>
            <dt>Category</dt>
            <dd className="poi-info-category">
              <PoiIconBadge
                iconKey={poi.categoryIconKey}
                color={poi.categoryColor}
                size={22}
                label={`${poi.categoryPath || 'POI'} marker`}
              />
              <span>{poi.categoryPath || '—'}</span>
            </dd>
          </div>
          <div>
            <dt>Working hours</dt>
            <dd>
              {formatTime(poi.openTime)} – {formatTime(poi.closeTime)}
            </dd>
          </div>
          <div>
            <dt>Added by</dt>
            <dd>{poi.createdBy || '—'}</dd>
          </div>
          <div>
            <dt>Added on</dt>
            <dd>{poi.createdAt ? new Date(poi.createdAt).toLocaleString() : '—'}</dd>
          </div>
        </dl>

        <div className="attr-modal-actions">
          {canDelete && (
            <button type="button" className="attr-modal-btn attr-modal-danger" onClick={onDelete}>
              Delete
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
