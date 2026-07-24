import { useEffect, useRef, useState } from 'react'
import { createPermission } from '../../api/admin'
import ModalCloseButton from '../../components/ModalCloseButton'

// Add a permission to the shared catalogue (name is the machine key, e.g. "report_export").
export default function PermissionFormModal({ onClose, onSuccess }) {
  const [name, setName] = useState('')
  const [description, setDescription] = useState('')
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const firstRef = useRef(null)

  useEffect(() => {
    firstRef.current?.focus()
  }, [])

  useEffect(() => {
    const onKey = (e) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const trimmed = name.trim()
  const canSubmit = trimmed.length > 0 && !submitting

  const handleSubmit = async (event) => {
    event.preventDefault()
    if (!canSubmit) return
    setSubmitting(true)
    setError('')
    try {
      await createPermission({ name: trimmed, description: description.trim() || null })
      onSuccess('Permission added.')
    } catch (err) {
      const status = err?.response?.status
      setError(
        status === 409
          ? err?.response?.data?.message ?? 'A permission with that name already exists.'
          : 'Could not add the permission.',
      )
      setSubmitting(false)
    }
  }

  return (
    <div className="admin-modal-overlay" role="dialog" aria-modal="true" aria-label="Add permission">
      <form className="admin-modal" onSubmit={handleSubmit}>
        <div className="admin-modal-head">
          <div>
            <h2 className="admin-modal-title">Add permission</h2>
            <p className="admin-modal-desc">Name is the machine key (e.g. add_point).</p>
          </div>
          <ModalCloseButton onClick={onClose} label="Close permission dialog" />
        </div>

        <div className="admin-modal-body">
          <label className="admin-field">
            <span>Name</span>
            <input
              ref={firstRef}
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. export_report"
              maxLength={80}
            />
          </label>

          <label className="admin-field">
            <span>Description (optional)</span>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="What this permission allows"
              maxLength={200}
            />
          </label>

          {error && <p className="admin-error">{error}</p>}
        </div>

        <div className="admin-modal-foot">
          <button type="submit" className="admin-btn admin-btn-primary" disabled={!canSubmit}>
            Add
          </button>
        </div>
      </form>
    </div>
  )
}
