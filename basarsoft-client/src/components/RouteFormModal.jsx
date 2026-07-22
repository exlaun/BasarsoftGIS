import { useEffect, useRef, useState } from 'react'
import './AttributeModal.css'

// Create/edit a transportation route (name + color). Modeled on AttributeModal (the drawn-shape
// name+color popup) but reused from the Route Management panel rather than the draw flow. onSave is
// awaited: on success the parent closes the modal; on failure this keeps the modal open and shows the
// error (e.g. a duplicate route name -> 409).
const DEFAULT_COLOR = '#2563eb'

export default function RouteFormModal({ mode, route, onSave, onCancel }) {
  const isEdit = mode === 'edit'
  const [name, setName] = useState(route?.name ?? '')
  const [color, setColor] = useState(route?.color ?? DEFAULT_COLOR)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState(null)
  const nameInputRef = useRef(null)

  useEffect(() => {
    nameInputRef.current?.focus()
  }, [])

  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  const trimmedName = name.trim()

  const handleSubmit = async (event) => {
    event.preventDefault()
    if (!trimmedName || submitting) return
    setSubmitting(true)
    setError(null)
    try {
      await onSave(trimmedName, color)
      // Parent closes the modal on success.
    } catch (err) {
      const code = err.response?.data?.code
      setError(
        code === 'duplicate_name'
          ? 'A route with that name already exists.'
          : 'Could not save the route. Please try again.',
      )
      setSubmitting(false)
    }
  }

  return (
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="Route details">
      <form className="attr-modal" onSubmit={handleSubmit}>
        <h2 className="attr-modal-title">{isEdit ? 'Edit route' : 'New route'}</h2>

        <label className="attr-modal-field">
          <span>Name *</span>
          <input
            ref={nameInputRef}
            type="text"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="e.g. Line 12"
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

        {error && <p className="attr-modal-message attr-modal-error">{error}</p>}

        <div className="attr-modal-actions">
          <button type="button" className="attr-modal-btn attr-modal-cancel" onClick={onCancel}>
            Cancel
          </button>
          <button
            type="submit"
            className="attr-modal-btn attr-modal-save"
            disabled={!trimmedName || submitting}
          >
            {submitting ? 'Saving…' : 'Save'}
          </button>
        </div>
      </form>
    </div>
  )
}
