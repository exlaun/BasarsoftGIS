import { useCallback, useEffect, useRef, useState } from 'react'
import { listRoles, listPermissions, deleteRole } from '../../api/admin'
import RoleFormModal from './RoleFormModal'
import RolePermissionsModal from './RolePermissionsModal'
import AdminConfirm from './AdminConfirm'
import { useAuth } from '../../context/auth-context'

export default function RolesPage() {
  const { refreshProfile } = useAuth()
  const [roles, setRoles] = useState([])
  const [permissions, setPermissions] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  const [modal, setModal] = useState(null) // { type: 'create'|'edit'|'perms'|'delete', role? }
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
    Promise.all([listRoles(), listPermissions()])
      .then(([r, p]) => {
        setRoles(r)
        setPermissions(p)
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
      await deleteRole(modal.role.id)
      closeAnd('Role deleted.')
    } catch {
      setModal(null)
      flash('error', 'Could not delete role.')
    }
  }

  return (
    <div>
      <div className="admin-page-head">
        <div>
          <h1 className="admin-page-title">Roles</h1>
          <p className="admin-page-sub">Group permissions into roles; users holding a role inherit them.</p>
        </div>
        <button type="button" className="admin-btn admin-btn-primary" onClick={() => setModal({ type: 'create' })}>
          + Add role
        </button>
      </div>

      <div className="admin-card">
        {loading ? (
          <p className="admin-loading">Loading…</p>
        ) : error ? (
          <p className="admin-empty">Could not load roles.</p>
        ) : roles.length === 0 ? (
          <p className="admin-empty">No roles yet.</p>
        ) : (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Description</th>
                  <th>Permissions</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {roles.map((r) => (
                  <tr key={r.id}>
                    <td>{r.name}</td>
                    <td className="admin-wrap">{r.description || <span className="admin-muted">—</span>}</td>
                    <td>{r.permissionIds.length}</td>
                    <td>
                      <div className="admin-table-actions">
                        <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'edit', role: r })}>
                          Edit
                        </button>
                        <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'perms', role: r })}>
                          Permissions
                        </button>
                        <button type="button" className="admin-btn admin-btn-sm admin-btn-danger" onClick={() => setModal({ type: 'delete', role: r })}>
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
        <RoleFormModal mode={modal.type} role={modal.role} onClose={() => setModal(null)} onSuccess={closeAnd} />
      )}
      {modal?.type === 'perms' && (
        <RolePermissionsModal role={modal.role} allPermissions={permissions} onClose={() => setModal(null)} onSuccess={closeAnd} />
      )}
      {modal?.type === 'delete' && (
        <AdminConfirm
          title="Delete role"
          message={`Delete role "${modal.role.name}"? It will be removed from every user who has it (soft delete).`}
          onConfirm={handleDelete}
          onCancel={() => setModal(null)}
        />
      )}

      {toast && <div className={`admin-toast admin-toast-${toast.type}`}>{toast.text}</div>}
    </div>
  )
}
