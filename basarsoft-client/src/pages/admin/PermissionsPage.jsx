import { useCallback, useEffect, useRef, useState } from 'react'
import { listPermissions, deletePermission } from '../../api/admin'
import PermissionFormModal from './PermissionFormModal'
import AdminConfirm from './AdminConfirm'
import { useAuth } from '../../context/auth-context'

export default function PermissionsPage() {
  const { refreshProfile } = useAuth()
  const [permissions, setPermissions] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  const [modal, setModal] = useState(null) // { type: 'create'|'delete', permission? }
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
    listPermissions()
      .then((data) => {
        setPermissions(data)
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
      await deletePermission(modal.permission.id)
      closeAnd('Permission deleted.')
    } catch {
      setModal(null)
      flash('error', 'Could not delete permission.')
    }
  }

  return (
    <div>
      <div className="admin-page-head">
        <div>
          <h1 className="admin-page-title">Permissions</h1>
          <p className="admin-page-sub">The shared catalogue assigned to roles and users.</p>
        </div>
        <button type="button" className="admin-btn admin-btn-primary" onClick={() => setModal({ type: 'create' })}>
          + Add permission
        </button>
      </div>

      <div className="admin-card">
        {loading ? (
          <p className="admin-loading">Loading…</p>
        ) : error ? (
          <p className="admin-empty">Could not load permissions.</p>
        ) : permissions.length === 0 ? (
          <p className="admin-empty">No permissions yet.</p>
        ) : (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Description</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {permissions.map((p) => (
                  <tr key={p.id}>
                    <td>{p.name}</td>
                    <td className="admin-wrap">{p.description || <span className="admin-muted">—</span>}</td>
                    <td>
                      <div className="admin-table-actions">
                        <button type="button" className="admin-btn admin-btn-sm admin-btn-danger" onClick={() => setModal({ type: 'delete', permission: p })}>
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

      {modal?.type === 'create' && (
        <PermissionFormModal onClose={() => setModal(null)} onSuccess={closeAnd} />
      )}
      {modal?.type === 'delete' && (
        <AdminConfirm
          title="Delete permission"
          message={`Delete permission "${modal.permission.name}"? It will be removed from every role and user that has it.`}
          onConfirm={handleDelete}
          onCancel={() => setModal(null)}
        />
      )}

      {toast && <div className={`admin-toast admin-toast-${toast.type}`}>{toast.text}</div>}
    </div>
  )
}
