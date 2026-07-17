import { useEffect } from 'react'
import './AttributeModal.css'
import './ShapePickerModal.css'
import PoiIconBadge from './PoiIconBadge'

const DEFAULT_COLOR = '#2563eb'

// Chooser shown when a click hits several overlapping map items: personal shapes use their color
// swatch while POIs use the effective category badge. Picking a row opens the matching info popup;
// Escape/Cancel closes without selecting anything.
export default function ShapePickerModal({ features, onPick, onCancel }) {
  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  return (
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="Select a map item">
      <div className="attr-modal">
        <h2 className="attr-modal-title">Select a map item</h2>
        <p className="shape-picker-subtitle">
          {features.length} items overlap here. Which one do you want to open?
        </p>

        <div className="shape-picker-list">
          {features.map((feature) => {
            const isPoi = feature.get('apiType') === 'poi'
            return (
              <button
                key={`${feature.get('apiType')}-${feature.get('dbId')}`}
                type="button"
                className="shape-picker-item"
                onClick={() => onPick(feature)}
              >
                {isPoi ? (
                  <PoiIconBadge
                    iconKey={feature.get('categoryIconKey')}
                    color={feature.get('categoryColor') || feature.get('color')}
                    size={20}
                    label={`${feature.get('categoryName') || 'POI'} marker`}
                  />
                ) : (
                  <span
                    className="shape-picker-swatch"
                    style={{ background: feature.get('color') || DEFAULT_COLOR }}
                    aria-hidden="true"
                  />
                )}
                <span className="shape-picker-name">{feature.get('name') ?? 'Unnamed'}</span>
                <span className="shape-picker-type">{isPoi ? 'POI' : feature.get('apiType')}</span>
              </button>
            )
          })}
        </div>

        <div className="attr-modal-actions">
          <button type="button" className="attr-modal-btn attr-modal-cancel" onClick={onCancel}>
            Cancel
          </button>
        </div>
      </div>
    </div>
  )
}
