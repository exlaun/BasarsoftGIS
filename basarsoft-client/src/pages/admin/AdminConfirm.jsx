import { useEffect, useRef } from 'react'

// Small confirmation dialog on the admin-modal pattern (fixed overlay). Cancel is autofocused and
// Escape cancels, so a stray Enter never triggers a destructive action.
export default function AdminConfirm({ title, message, confirmLabel = 'Delete', onConfirm, onCancel }) {
  const cancelRef = useRef(null)

  useEffect(() => {
    cancelRef.current?.focus()
  }, [])

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
        </div>
        <div className="admin-modal-body">
          <p style={{ margin: 0, opacity: 0.85, lineHeight: 1.5 }}>{message}</p>
        </div>
        <div className="admin-modal-foot">
          <button ref={cancelRef} type="button" className="admin-btn" onClick={onCancel}>
            Cancel
          </button>
          <button type="button" className="admin-btn admin-btn-danger" onClick={onConfirm}>
            {confirmLabel}
          </button>
        </div>
      </div>
    </div>
  )
}
