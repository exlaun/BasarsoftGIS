import { useEffect } from 'react'
import './AttributeModal.css'
import ModalCloseButton from './ModalCloseButton'

// Generic confirmation dialog on the shared attr-modal pattern. Used before destructive actions
// (currently: soft-deleting a shape). Escape and the header X dismiss it without confirming.
export default function ConfirmModal({ title, message, confirmLabel = 'Delete', onConfirm, onCancel }) {
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
        <div className="attr-modal-head">
          <h2 className="attr-modal-title">{title}</h2>
          <ModalCloseButton onClick={onCancel} label="Close confirmation" />
        </div>
        <p className="attr-modal-message">{message}</p>
        <div className="attr-modal-actions">
          <button type="button" className="attr-modal-btn attr-modal-danger" onClick={onConfirm}>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
