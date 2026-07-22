import { useEffect, useRef, useState } from 'react'
import './AttributeModal.css'
import './ShapeInfoModal.css'

const DEFAULT_COLOR = '#2563eb'

// Popup shown when an existing shape is clicked (Select tool). Everyone can inspect the details.
// Callers that still hold the matching geometry permission also get the name/color and move controls;
// Viewer and users whose permission was removed get a genuinely read-only detail view.
// `canDelete` is separate from `canEdit` because deletion carries the extra geographic-authorization
// rule: a shape outside the caller's area cannot be removed even with the right permission. It
// defaults to `canEdit` so callers that don't distinguish the two keep the old behavior.
export default function ShapeInfoModal({
  type,
  initialName,
  initialColor,
  modifiedDate,
  modifiedUserId,
  canEdit,
  canDelete = canEdit,
  onSave,
  onEditLocation,
  onDelete,
  onCancel,
}) {
  const [name, setName] = useState(initialName ?? '')
  const [color, setColor] = useState(initialColor || DEFAULT_COLOR)
  const nameInputRef = useRef(null)

  useEffect(() => {
    if (canEdit) nameInputRef.current?.focus()
  }, [canEdit])

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
    if (!canEdit || !trimmedName) return
    onSave(trimmedName, color)
  }

  const handleEditLocation = () => {
    if (!canEdit || !trimmedName) return
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
        <h2 className="attr-modal-title">{canEdit ? 'Edit shape' : 'Shape details'}</h2>

        <dl className="shape-info-meta">
          <div>
            <dt>Type</dt>
            <dd className="shape-info-type">{type}</dd>
          </div>
          {!canEdit && (
            <>
              <div className="shape-info-name">
                <dt>Name</dt>
                <dd>{initialName || 'Unnamed shape'}</dd>
              </div>
              <div>
                <dt>Color</dt>
                <dd className="shape-info-color">
                  <span
                    className="shape-info-color-swatch"
                    style={{ backgroundColor: color }}
                    aria-hidden="true"
                  />
                  {color}
                </dd>
              </div>
            </>
          )}
          {modifiedText && (
            <div>
              <dt>Last edited</dt>
              <dd>{modifiedText}</dd>
            </div>
          )}
        </dl>

        {canEdit && (
          <>
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

            <button
              type="button"
              className="attr-modal-btn shape-info-edit-location"
              onClick={handleEditLocation}
              disabled={!trimmedName}
            >
              Edit location on map
            </button>
          </>
        )}

        <div className="attr-modal-actions">
          {canDelete && (
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
          )}
          <button type="button" className="attr-modal-btn attr-modal-cancel" onClick={onCancel}>
            {canEdit ? 'Cancel' : 'Close'}
          </button>
          {canEdit && (
            <button
              type="submit"
              className="attr-modal-btn attr-modal-save"
              disabled={!trimmedName}
            >
              Save
            </button>
          )}
        </div>
      </form>
    </div>
  )
}
