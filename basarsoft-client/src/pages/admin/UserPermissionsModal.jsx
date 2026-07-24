import { useEffect, useState } from 'react'
import { getUserPermissions, setUserPermissions } from '../../api/admin'
import ModalCloseButton from '../../components/ModalCloseButton'

// The inheritance-aware permission editor — the literal implementation of the mentor's rule.
// Every catalogue permission is shown with its source:
//   • source "role"  -> inherited from a role: the checkbox is CHECKED + DISABLED, with a
//                       "From role: <RoleName>" badge, so the admin can't re-pick what already comes from
//                       the role.
//   • source "direct"-> granted straight to the user: a normal ticked, toggleable checkbox.
//   • source "none"  -> not granted: an empty, toggleable checkbox.
// Saving sends only the DIRECT grants; role-derived permissions are managed via the role.
export default function UserPermissionsModal({ user, onClose, onSuccess }) {
  const [perms, setPerms] = useState(null)
  const [direct, setDirect] = useState(() => new Set())
  const [loading, setLoading] = useState(true)
  const [loadError, setLoadError] = useState(false)
  const [submitting, setSubmitting] = useState(false)
  const [saveError, setSaveError] = useState('')

  useEffect(() => {
    const onKey = (e) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  useEffect(() => {
    let cancelled = false
    getUserPermissions(user.id)
      .then((data) => {
        if (cancelled) return
        setPerms(data)
        // Seed the toggle set with whatever is already a direct grant (including any that are also
        // inherited — those stay disabled but we must keep them so saving doesn't drop the direct grant).
        setDirect(new Set(data.filter((p) => p.isDirect).map((p) => p.permissionId)))
        setLoading(false)
      })
      .catch(() => {
        if (!cancelled) {
          setLoadError(true)
          setLoading(false)
        }
      })
    return () => {
      cancelled = true
    }
  }, [user.id])

  const toggle = (id) =>
    setDirect((prev) => {
      const next = new Set(prev)
      next.has(id) ? next.delete(id) : next.add(id)
      return next
    })

  const handleSave = async () => {
    setSubmitting(true)
    setSaveError('')
    try {
      await setUserPermissions(user.id, [...direct])
      onSuccess('Permissions updated.')
    } catch {
      setSaveError('Could not update permissions.')
      setSubmitting(false)
    }
  }

  return (
    <div className="admin-modal-overlay" role="dialog" aria-modal="true" aria-label="Manage user permissions">
      <div className="admin-modal admin-modal-wide">
        <div className="admin-modal-head">
          <div>
            <h2 className="admin-modal-title">Permissions — {user.username}</h2>
            <p className="admin-modal-desc">
              Permissions that come from a role are locked and marked <em>From role</em>. Tick the rest to grant
              them directly to this user.
            </p>
          </div>
          <ModalCloseButton onClick={onClose} label="Close user permissions dialog" />
        </div>

        <div className="admin-modal-body admin-permissions-body" tabIndex="0" aria-label="Permission list">
          {loading && <p className="admin-empty">Loading…</p>}
          {loadError && <p className="admin-error">Could not load permissions.</p>}
          {perms && (
            <div className="admin-checklist">
              {perms.map((p) => {
                const inherited = p.source === 'role'
                const checked = inherited ? true : direct.has(p.permissionId)
                return (
                  <label
                    key={p.permissionId}
                    className={`admin-check-row${inherited ? ' is-inherited' : ''}`}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      disabled={inherited}
                      onChange={() => toggle(p.permissionId)}
                    />
                    <div className="admin-check-main">
                      <div className="admin-check-name">
                        {p.name}
                        {inherited && (
                          <span className="admin-badge admin-badge-role">From role: {p.roleName}</span>
                        )}
                        {!inherited && p.source === 'direct' && (
                          <span className="admin-badge admin-badge-direct">Direct</span>
                        )}
                      </div>
                      {p.description && <div className="admin-check-desc">{p.description}</div>}
                    </div>
                  </label>
                )
              })}
            </div>
          )}
          {saveError && <p className="admin-error">{saveError}</p>}
        </div>

        <div className="admin-modal-foot">
          <button
            type="button"
            className="admin-btn admin-btn-primary"
            onClick={handleSave}
            disabled={submitting || loading || loadError}
          >
            Save
          </button>
        </div>
      </div>
    </div>
  )
}
