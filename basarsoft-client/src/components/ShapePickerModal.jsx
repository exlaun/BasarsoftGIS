import { useEffect } from 'react'
import './AttributeModal.css'
import './ShapePickerModal.css'

const DEFAULT_COLOR = '#2563eb'

// Chooser shown when a click hits several overlapping shapes: one row per shape (color swatch,
// name, type), rendered in hit order (topmost first). Picking a row opens that shape's info popup;
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
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="Select a shape">
      <div className="attr-modal">
        <h2 className="attr-modal-title">Select a shape</h2>
        <p className="shape-picker-subtitle">
          {features.length} shapes overlap here. Which one do you want to open?
        </p>

        <div className="shape-picker-list">
          {features.map((feature) => (
            <button
              key={`${feature.get('apiType')}-${feature.get('dbId')}`}
              type="button"
              className="shape-picker-item"
              onClick={() => onPick(feature)}
            >
              <span
                className="shape-picker-swatch"
                style={{ background: feature.get('color') || DEFAULT_COLOR }}
                aria-hidden="true"
              />
              <span className="shape-picker-name">{feature.get('name') ?? 'Unnamed'}</span>
              <span className="shape-picker-type">{feature.get('apiType')}</span>
            </button>
          ))}
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
