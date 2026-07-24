import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { listRoles, listPermissions, deleteRole } from '../../api/admin'
import AdminTable from '../../components/AdminTable'
import RoleFormModal from './RoleFormModal'
import RolePermissionsModal from './RolePermissionsModal'
import GeoAuthModal from './GeoAuthModal'
import AdminConfirm from './AdminConfirm'
import { useAuth } from '../../context/auth-context'

export default function RolesPage() {
  const { refreshProfile, permissions: grantedPermissions } = useAuth()
  const canManagePermissions = grantedPermissions.includes('manage_permissions')
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
    // Roles are the primary resource. Permission-catalogue access is a separate permission, so a
    // manage_roles-only account must still receive its roles table when that optional request is 403.
    listRoles()
      .then((data) => {
        setRoles(data)
        setError(false)
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false))

    if (canManagePermissions) {
      listPermissions()
        .then(setPermissions)
        .catch(() => setPermissions([]))
    } else {
      Promise.resolve().then(() => setPermissions([]))
    }
  }, [canManagePermissions])

  useEffect(() => {
    load()
  }, [load])

  const closeAnd = (message) => {
    setModal(null)
    if (message) flash('success', message)
    setLoading(true)
    setError(false)
    load()
    refreshProfile()
  }

  const handleDelete = async () => {
    try {
      await deleteRole(modal.role.id)
      closeAnd('Role deleted.')
    } catch (err) {
      setModal(null)
      // The API refuses deleting a role whose loss would leave no active admin — show its reason.
      flash('error', err.response?.data?.message ?? 'Could not delete role.')
    }
  }

  const roleColumns = useMemo(() => [
    { key: 'name', label: 'Name', sortValue: (role) => role.name, flex: 0.9, minWidth: 130 },
    {
      key: 'description',
      label: 'Description',
      sortValue: (role) => role.description,
      flex: 1.4,
      minWidth: 180,
      cellClassName: 'admin-wrap',
      render: (role) => role.description || <span className="admin-muted">—</span>,
    },
    {
      key: 'permissionCount',
      label: 'Permissions',
      sortType: 'number',
      sortValue: (role) => role.permissionIds?.length ?? 0,
      flex: 0.65,
      minWidth: 120,
      render: (role) => role.permissionIds?.length ?? 0,
    },
    {
      key: 'actions',
      label: 'Actions',
      fixedWidth: 300,
      sortable: false,
      resizable: false,
      align: 'right',
      render: (role) => (
        <div className="admin-table-actions">
          <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'edit', role })}>
            Edit
          </button>
          {canManagePermissions && (
            <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'perms', role })}>
              Permissions
            </button>
          )}
          <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'geo', role })}>
            Geographic Access
          </button>
          <button type="button" className="admin-btn admin-btn-sm admin-btn-danger" onClick={() => setModal({ type: 'delete', role })}>
            Delete
          </button>
        </div>
      ),
    },
  ], [canManagePermissions])

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
          <AdminTable
            columns={roleColumns}
            rows={roles}
            getRowKey={(role) => role.id}
            defaultSortKey="name"
            defaultSortDir="asc"
          />
        )}
      </div>

      {(modal?.type === 'create' || modal?.type === 'edit') && (
        <RoleFormModal mode={modal.type} role={modal.role} onClose={() => setModal(null)} onSuccess={closeAnd} />
      )}
      {modal?.type === 'perms' && (
        <RolePermissionsModal role={modal.role} allPermissions={permissions} onClose={() => setModal(null)} onSuccess={closeAnd} />
      )}
      {modal?.type === 'geo' && (
        <GeoAuthModal
          kind="role"
          targetId={modal.role.id}
          targetLabel={modal.role.name}
          onClose={() => setModal(null)}
          onSuccess={closeAnd}
        />
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
