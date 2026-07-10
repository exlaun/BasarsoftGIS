import { useEffect, useRef, useState } from 'react'
import { createUser, updateUser } from '../../api/admin'

// Create or edit a user. In create mode you set a username + password and can optionally tick roles to
// grant right away; in edit mode you can rename, toggle active, and optionally reset the password
// (leave blank to keep the current one). Role editing after creation lives in UserRolesModal.
export default function UserFormModal({ mode, user, roles, onClose, onSuccess }) {
  const isEdit = mode === 'edit'
  const [username, setUsername] = useState(user?.username ?? '')
  const [password, setPassword] = useState('')
  const [isActive, setIsActive] = useState(user?.isActive ?? true)
  const [roleIds, setRoleIds] = useState(() => new Set())
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

  const toggleRole = (id) =>
    setRoleIds((prev) => {
      const next = new Set(prev)
      next.has(id) ? next.delete(id) : next.add(id)
      return next
    })

  const trimmed = username.trim()
  const canSubmit =
    trimmed.length >= 3 && (isEdit ? true : password.length >= 6) && !submitting

  const handleSubmit = async (event) => {
    event.preventDefault()
    if (!canSubmit) return
    setSubmitting(true)
    setError('')
    try {
      if (isEdit) {
        await updateUser(user.id, {
          username: trimmed,
          isActive,
          newPassword: password.trim() ? password : null,
        })
        onSuccess('User updated.')
      } else {
        await createUser({ username: trimmed, password, roleIds: [...roleIds] })
        onSuccess('User created.')
      }
    } catch (err) {
      const status = err?.response?.status
      setError(
        status === 409
          ? err?.response?.data?.message ?? 'Username is already taken.'
          : 'Could not save the user.',
      )
      setSubmitting(false)
    }
  }

  return (
    <div className="admin-modal-overlay" role="dialog" aria-modal="true" aria-label={isEdit ? 'Edit user' : 'Add user'}>
      <form className="admin-modal" onSubmit={handleSubmit}>
        <div className="admin-modal-head">
          <h2 className="admin-modal-title">{isEdit ? 'Edit user' : 'Add user'}</h2>
        </div>

        <div className="admin-modal-body">
          <label className="admin-field">
            <span>Username</span>
            <input
              ref={firstRef}
              type="text"
              value={username}
              onChange={(e) => setUsername(e.target.value)}
              placeholder="e.g. alex"
              maxLength={50}
            />
          </label>

          <label className="admin-field">
            <span>{isEdit ? 'New password (leave blank to keep)' : 'Password'}</span>
            <input
              type="password"
              value={password}
              onChange={(e) => setPassword(e.target.value)}
              placeholder={isEdit ? '••••••' : 'At least 6 characters'}
              autoComplete="new-password"
            />
          </label>

          {isEdit && (
            <label className="admin-field admin-field-check">
              <input type="checkbox" checked={isActive} onChange={(e) => setIsActive(e.target.checked)} />
              <span>Active (can log in)</span>
            </label>
          )}

          {!isEdit && roles.length > 0 && (
            <div className="admin-field">
              <span>Roles (optional)</span>
              <div className="admin-checklist">
                {roles.map((r) => (
                  <label key={r.id} className="admin-check-row">
                    <input type="checkbox" checked={roleIds.has(r.id)} onChange={() => toggleRole(r.id)} />
                    <div className="admin-check-main">
                      <div className="admin-check-name">{r.name}</div>
                      {r.description && <div className="admin-check-desc">{r.description}</div>}
                    </div>
                  </label>
                ))}
              </div>
            </div>
          )}

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
