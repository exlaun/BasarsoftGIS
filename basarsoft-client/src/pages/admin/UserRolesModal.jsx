import { useEffect, useState } from 'react'
import { setUserRoles } from '../../api/admin'
import ModalCloseButton from '../../components/ModalCloseButton'

// Assign roles to a user: a checkbox per role, pre-ticked for the ones the user already holds. Saving
// replaces the user's role set. Whatever permissions those roles grant then flow to the user as
// inherited (visible in UserPermissionsModal).
export default function UserRolesModal({ user, allRoles, onClose, onSuccess }) {
  const [selected, setSelected] = useState(() => new Set((user.roles ?? []).map((r) => r.id)))
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')

  useEffect(() => {
    const onKey = (e) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const toggle = (id) =>
    setSelected((prev) => {
      const next = new Set(prev)
      next.has(id) ? next.delete(id) : next.add(id)
      return next
    })

  const handleSave = async () => {
    setSubmitting(true)
    setError('')
    try {
      await setUserRoles(user.id, [...selected])
      onSuccess('Roles updated.')
    } catch {
      setError('Could not update roles.')
      setSubmitting(false)
    }
  }

  return (
    <div className="admin-modal-overlay" role="dialog" aria-modal="true" aria-label="Manage roles">
      <div className="admin-modal">
        <div className="admin-modal-head">
          <div>
            <h2 className="admin-modal-title">Roles — {user.username}</h2>
            <p className="admin-modal-desc">Tick the roles this user should have.</p>
          </div>
          <ModalCloseButton onClick={onClose} label="Close user roles dialog" />
        </div>

        <div className="admin-modal-body">
          {allRoles.length === 0 ? (
            <p className="admin-empty">No roles defined yet.</p>
          ) : (
            <div className="admin-checklist">
              {allRoles.map((r) => (
                <label key={r.id} className="admin-check-row">
                  <input type="checkbox" checked={selected.has(r.id)} onChange={() => toggle(r.id)} />
                  <div className="admin-check-main">
                    <div className="admin-check-name">{r.name}</div>
                    {r.description && <div className="admin-check-desc">{r.description}</div>}
                  </div>
                </label>
              ))}
            </div>
          )}
          {error && <p className="admin-error">{error}</p>}
        </div>

        <div className="admin-modal-foot">
          <button type="button" className="admin-btn admin-btn-primary" onClick={handleSave} disabled={submitting}>
            Save
          </button>
        </div>
      </div>
    </div>
  )
}
