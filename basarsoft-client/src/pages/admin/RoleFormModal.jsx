import { useEffect, useRef, useState } from 'react'
import { createRole, updateRole } from '../../api/admin'

// Create or edit a role (name + description). Permission assignment lives in RolePermissionsModal.
export default function RoleFormModal({ mode, role, onClose, onSuccess }) {
  const isEdit = mode === 'edit'
  const [name, setName] = useState(role?.name ?? '')
  const [description, setDescription] = useState(role?.description ?? '')
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
      const body = { name: trimmed, description: description.trim() || null }
      if (isEdit) {
        await updateRole(role.id, body)
        onSuccess('Role updated.')
      } else {
        await createRole(body)
        onSuccess('Role created.')
      }
    } catch (err) {
      const status = err?.response?.status
      setError(
        status === 409
          ? err?.response?.data?.message ?? 'A role with that name already exists.'
          : 'Could not save the role.',
      )
      setSubmitting(false)
    }
  }

  return (
    <div className="admin-modal-overlay" role="dialog" aria-modal="true" aria-label={isEdit ? 'Edit role' : 'Add role'}>
      <form className="admin-modal" onSubmit={handleSubmit}>
        <div className="admin-modal-head">
          <h2 className="admin-modal-title">{isEdit ? 'Edit role' : 'Add role'}</h2>
        </div>

        <div className="admin-modal-body">
          <label className="admin-field">
            <span>Name</span>
            <input
              ref={firstRef}
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Editor"
              maxLength={80}
            />
          </label>

          <label className="admin-field">
            <span>Description (optional)</span>
            <textarea
              value={description}
              onChange={(e) => setDescription(e.target.value)}
              placeholder="What this role is for"
              maxLength={200}
            />
          </label>

          {error && <p className="admin-error">{error}</p>}
        </div>

        <div className="admin-modal-foot">
          <button type="button" className="admin-btn" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="admin-btn admin-btn-primary" disabled={!canSubmit}>
            {isEdit ? 'Save' : 'Create'}
          </button>
        </div>
      </form>
    </div>
  )
}
