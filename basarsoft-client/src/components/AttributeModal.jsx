import { useEffect, useRef, useState } from 'react'
import './AttributeModal.css'

// Popup shown after a shape is drawn (drawend). Collects the shape's attributes — Name and Color —
// before it is saved to the database. Save passes them up; Cancel discards the drawn shape.
const DEFAULT_COLOR = '#2563eb'

export default function AttributeModal({ onSave, onCancel }) {
  const [name, setName] = useState('')
  const [color, setColor] = useState(DEFAULT_COLOR)
  const nameInputRef = useRef(null)

  // Focus the name field as soon as the popup opens so the user can type immediately.
  useEffect(() => {
    nameInputRef.current?.focus()
  }, [])

  // Escape closes the popup (same as Cancel — discards the drawing).
  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  // Name is required (mentor's spec: "at least Name and Color"). Bail on an empty/whitespace name;
  // the Save button is also disabled in that state, so this is a belt-and-braces guard.
  const trimmedName = name.trim()
  const handleSubmit = (event) => {
    event.preventDefault()
    if (!trimmedName) return
    onSave(trimmedName, color)
  }

  return (
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="Shape details">
      <form className="attr-modal" onSubmit={handleSubmit}>
        <h2 className="attr-modal-title">Shape details</h2>

        <label className="attr-modal-field">
          <span>Name *</span>
          <input
            ref={nameInputRef}
            type="text"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="e.g. Favorite Map Feature"
            maxLength={80}
          />
        </label>

        <label className="attr-modal-field attr-modal-color">
          <span>Color</span>
          <input
            type="color"
            value={color}
            onChange={(event) => setColor(event.target.value)}
          />
        </label>

        <div className="attr-modal-actions">
          <button type="button" className="attr-modal-btn attr-modal-cancel" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="submit"
            className="attr-modal-btn attr-modal-save"
            disabled={!trimmedName}
          >
            Save
          </button>
        </div>
      </form>
    </div>
  )
}
