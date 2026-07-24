import { useEffect } from 'react'
import ModalCloseButton from '../../components/ModalCloseButton'

// Small confirmation dialog on the admin-modal pattern (fixed overlay). Escape and the header X
// dismiss it, so a stray Enter never triggers a destructive action.
export default function AdminConfirm({ title, message, confirmLabel = 'Delete', onConfirm, onCancel }) {
  useEffect(() => {
    const onKey = (event) => {
      if (event.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onCancel])

  return (
    <div className="admin-modal-overlay" role="alertdialog" aria-modal="true" aria-label={title}>
      <div className="admin-modal">
        <div className="admin-modal-head">
          <h2 className="admin-modal-title">{title}</h2>
          <ModalCloseButton onClick={onCancel} label="Close confirmation" />
        </div>
        <div className="admin-modal-body">
          <p style={{ margin: 0, opacity: 0.85, lineHeight: 1.5 }}>{message}</p>
        </div>
        <div className="admin-modal-foot">
          <button type="button" className="admin-btn admin-btn-danger" onClick={onConfirm}>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
