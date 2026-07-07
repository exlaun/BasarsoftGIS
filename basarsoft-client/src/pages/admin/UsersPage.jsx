import { useCallback, useEffect, useRef, useState } from 'react'
import { listUsers, listRoles, deleteUser } from '../../api/admin'
import UserFormModal from './UserFormModal'
import UserRolesModal from './UserRolesModal'
import UserPermissionsModal from './UserPermissionsModal'
import AdminConfirm from './AdminConfirm'
import { useAuth } from '../../context/auth-context'

export default function UsersPage() {
  const { refreshProfile } = useAuth()
  const [users, setUsers] = useState([])
  const [roles, setRoles] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  const [modal, setModal] = useState(null) // { type: 'create'|'edit'|'roles'|'perms'|'delete', user? }
  const [toast, setToast] = useState(null)
  const toastTimer = useRef()

  const flash = useCallback((type, text) => {
    setToast({ type, text })
    clearTimeout(toastTimer.current)
    toastTimer.current = setTimeout(() => setToast(null), 2600)
  }, [])

  useEffect(() => () => clearTimeout(toastTimer.current), [])

  // setState lives only in the async callbacks (not the effect body) to satisfy the hooks lint rule.
  const load = useCallback(() => {
    Promise.all([listUsers(), listRoles()])
      .then(([u, r]) => {
        setUsers(u)
        setRoles(r)
        setError(false)
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  const closeAnd = (message) => {
    setModal(null)
    if (message) flash('success', message)
    load()
    refreshProfile()
  }

  const handleDelete = async () => {
    try {
      await deleteUser(modal.user.id)
      closeAnd('User deleted.')
    } catch {
      setModal(null)
      flash('error', 'Could not delete user.')
    }
  }

  return (
    <div>
      <div className="admin-page-head">
        <div>
          <h1 className="admin-page-title">Users</h1>
          <p className="admin-page-sub">Add, edit and remove users; assign roles and direct permissions.</p>
        </div>
        <button type="button" className="admin-btn admin-btn-primary" onClick={() => setModal({ type: 'create' })}>
          + Add user
        </button>
      </div>

      <div className="admin-card">
        {loading ? (
          <p className="admin-loading">Loading…</p>
        ) : error ? (
          <p className="admin-empty">Could not load users.</p>
        ) : users.length === 0 ? (
          <p className="admin-empty">No users yet.</p>
        ) : (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Username</th>
                  <th>Status</th>
                  <th>Roles</th>
                  <th>Created</th>
                  <th>Modified</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {users.map((u) => (
                  <tr key={u.id}>
                    <td>{u.username}</td>
                    <td>
                      <span className="admin-status">
                        <span className={`admin-dot ${u.isActive ? 'admin-dot-on' : 'admin-dot-off'}`} />
                        {u.isActive ? 'Active' : 'Disabled'}
                      </span>
                    </td>
                    <td>
                      {u.roles.length === 0 ? (
                        <span className="admin-muted">—</span>
                      ) : (
                        <span className="admin-badge-list">
                          {u.roles.map((r) => (
                            <span key={r.id} className="admin-badge admin-badge-role">
                              {r.name}
                            </span>
                          ))}
                        </span>
                      )}
                    </td>
                    <td>{new Date(u.createdAt).toLocaleDateString()}</td>
                    <td>{new Date(u.modifiedDate).toLocaleDateString()}</td>
                    <td>
                      <div className="admin-table-actions">
                        <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'edit', user: u })}>
                          Edit
                        </button>
                        <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'roles', user: u })}>
                          Roles
                        </button>
                        <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'perms', user: u })}>
                          Permissions
                        </button>
                        <button type="button" className="admin-btn admin-btn-sm admin-btn-danger" onClick={() => setModal({ type: 'delete', user: u })}>
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {(modal?.type === 'create' || modal?.type === 'edit') && (
        <UserFormModal
          mode={modal.type}
          user={modal.user}
          roles={roles}
          onClose={() => setModal(null)}
          onSuccess={closeAnd}
        />
      )}
      {modal?.type === 'roles' && (
        <UserRolesModal user={modal.user} allRoles={roles} onClose={() => setModal(null)} onSuccess={closeAnd} />
      )}
      {modal?.type === 'perms' && (
        <UserPermissionsModal user={modal.user} onClose={() => setModal(null)} onSuccess={closeAnd} />
      )}
      {modal?.type === 'delete' && (
        <AdminConfirm
          title="Delete user"
          message={`Delete "${modal.user.username}"? They will be hidden (soft delete) and their role/permission links removed.`}
          onConfirm={handleDelete}
          onCancel={() => setModal(null)}
        />
      )}

      {toast && <div className={`admin-toast admin-toast-${toast.type}`}>{toast.text}</div>}
    </div>
  )
}
