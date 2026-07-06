import { useEffect, useRef, useState } from 'react'
import './AttributeModal.css'
import './ShapeInfoModal.css'

const DEFAULT_COLOR = '#2563eb'

// Popup shown when an existing shape is clicked (Select tool). Displays the shape's type + last-edited
// time/user, lets the user edit its Name and Color, and offers "Edit location on map" to reposition the
// geometry. Save persists name+color; Edit location hands the current field values up and switches the
// map into geometry-drag mode; Delete asks the parent to confirm a soft delete; Cancel closes without
// changes.
export default function ShapeInfoModal({
  type,
  initialName,
  initialColor,
  modifiedDate,
  modifiedUserId,
  onSave,
  onEditLocation,
  onDelete,
  onCancel,
}) {
  const [name, setName] = useState(initialName ?? '')
  const [color, setColor] = useState(initialColor || DEFAULT_COLOR)
  const nameInputRef = useRef(null)

  useEffect(() => {
    nameInputRef.current?.focus()
  }, [])

  // Escape closes the popup (same as Cancel).
  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  const trimmedName = name.trim()

  const handleSubmit = (event) => {
    event.preventDefault()
    if (!trimmedName) return
    onSave(trimmedName, color)
  }

  const handleEditLocation = () => {
    if (!trimmedName) return
    onEditLocation(trimmedName, color)
  }

  // "when · by whom" — modified_user_id is the audit companion of the timestamp. Within the current
  // per-user isolation the editor is always the owner; showing the id is the visible proof it's stored.
  const modifiedText = modifiedDate
    ? `${new Date(modifiedDate).toLocaleString()}${modifiedUserId != null ? ` · by user #${modifiedUserId}` : ''}`
    : null

  return (
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="Shape details">
      <form className="attr-modal" onSubmit={handleSubmit}>
        <h2 className="attr-modal-title">Edit shape</h2>

        <dl className="shape-info-meta">
          <div>
            <dt>Type</dt>
            <dd className="shape-info-type">{type}</dd>
          </div>
          {modifiedText && (
            <div>
              <dt>Last edited</dt>
              <dd>{modifiedText}</dd>
            </div>
          )}
        </dl>

        <label className="attr-modal-field">
          <span>Name *</span>
          <input
            ref={nameInputRef}
            type="text"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="e.g. Warehouse A"
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

        <button
          type="button"
          className="attr-modal-btn shape-info-edit-location"
          onClick={handleEditLocation}
          disabled={!trimmedName}
        >
          Edit location on map
        </button>

        <div className="attr-modal-actions">
          <button
            type="button"
            className="attr-modal-btn attr-modal-danger shape-info-delete"
            onClick={onDelete}
          >
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2m3 0v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" />
              <path d="M10 11v6M14 11v6" />
            </svg>
            Delete
          </button>
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
