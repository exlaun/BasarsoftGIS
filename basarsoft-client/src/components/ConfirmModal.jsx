import { useEffect, useRef } from 'react'
import './AttributeModal.css'

// Generic confirmation dialog on the shared attr-modal pattern. Used before destructive actions
// (currently: soft-deleting a shape). Escape = cancel; Cancel is autofocused so a stray Enter
// never confirms a destructive action by accident.
export default function ConfirmModal({ title, message, confirmLabel = 'Delete', onConfirm, onCancel }) {
  const cancelButtonRef = useRef(null)

  useEffect(() => {
    cancelButtonRef.current?.focus()
  }, [])

  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  return (
    <div className="attr-modal-overlay" role="alertdialog" aria-modal="true" aria-label={title}>
      <div className="attr-modal">
        <h2 className="attr-modal-title">{title}</h2>
        <p className="attr-modal-message">{message}</p>
        <div className="attr-modal-actions">
          <button
            ref={cancelButtonRef}
            type="button"
            className="attr-modal-btn attr-modal-cancel"
            onClick={onCancel}
          >
            Cancel
          </button>
          <button type="button" className="attr-modal-btn attr-modal-danger" onClick={onConfirm}>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
