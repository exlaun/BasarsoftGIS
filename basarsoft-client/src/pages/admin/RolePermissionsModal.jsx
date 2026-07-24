import { useEffect, useState } from 'react'
import { setRolePermissions } from '../../api/admin'
import ModalCloseButton from '../../components/ModalCloseButton'

// Assign permissions to a role: a checkbox per catalogue permission, pre-ticked for the ones the role
// already grants. Every user holding this role inherits the selected permissions.
export default function RolePermissionsModal({ role, allPermissions, onClose, onSuccess }) {
  const [selected, setSelected] = useState(() => new Set(role.permissionIds ?? []))
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
      await setRolePermissions(role.id, [...selected])
      onSuccess('Role permissions updated.')
    } catch {
      setError('Could not update permissions.')
      setSubmitting(false)
    }
  }

  return (
    <div className="admin-modal-overlay" role="dialog" aria-modal="true" aria-label="Manage role permissions">
      <div className="admin-modal admin-modal-wide">
        <div className="admin-modal-head">
          <div>
            <h2 className="admin-modal-title">Permissions — {role.name}</h2>
            <p className="admin-modal-desc">Users with this role inherit the ticked permissions.</p>
          </div>
          <ModalCloseButton onClick={onClose} label="Close role permissions dialog" />
        </div>

        <div className="admin-modal-body">
          {allPermissions.length === 0 ? (
            <p className="admin-empty">No permissions defined yet.</p>
          ) : (
            <div className="admin-checklist">
              {allPermissions.map((p) => (
                <label key={p.id} className="admin-check-row">
                  <input type="checkbox" checked={selected.has(p.id)} onChange={() => toggle(p.id)} />
                  <div className="admin-check-main">
                    <div className="admin-check-name">{p.name}</div>
                    {p.description && <div className="admin-check-desc">{p.description}</div>}
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
