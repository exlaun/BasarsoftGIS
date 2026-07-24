import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { listUsers, listRoles, deleteUser } from '../../api/admin'
import AdminTable from '../../components/AdminTable'
import UserFormModal from './UserFormModal'
import UserRolesModal from './UserRolesModal'
import UserPermissionsModal from './UserPermissionsModal'
import GeoAuthModal from './GeoAuthModal'
import AdminConfirm from './AdminConfirm'
import { useAuth } from '../../context/auth-context'

export default function UsersPage() {
  const { refreshProfile, userId, permissions } = useAuth()
  const canManageRoles = permissions.includes('manage_roles')
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
    // Users are the primary resource for this page. A manage_users-only account is not allowed to
    // call the roles catalogue, so a denied optional roles request must never hide the users table.
    listUsers()
      .then((data) => {
        setUsers(data)
        setError(false)
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false))

    if (canManageRoles) {
      listRoles()
        .then(setRoles)
        .catch(() => setRoles([]))
    } else {
      Promise.resolve().then(() => setRoles([]))
    }
  }, [canManageRoles])

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
      await deleteUser(modal.user.id)
      closeAnd('User deleted.')
    } catch (err) {
      setModal(null)
      // The API refuses some deletes with a reason (own account, last active admin) — show it.
      flash('error', err.response?.data?.message ?? 'Could not delete user.')
    }
  }

  const userColumns = useMemo(() => [
    { key: 'username', label: 'Username', sortValue: (user) => user.username, flex: 0.95, minWidth: 120 },
    {
      key: 'status',
      label: 'Status',
      sortType: 'number',
      sortValue: (user) => (user.isActive ? 0 : 1),
      flex: 0.7,
      minWidth: 100,
      render: (user) => (
        <span className="admin-status">
          <span className={`admin-dot ${user.isActive ? 'admin-dot-on' : 'admin-dot-off'}`} />
          {user.isActive ? 'Active' : 'Disabled'}
        </span>
      ),
    },
    {
      key: 'roles',
      label: 'Roles',
      sortType: 'number',
      sortValue: (user) => user.roles?.length ?? 0,
      flex: 1.2,
      minWidth: 150,
      render: (user) => user.roles?.length === 0 ? (
        <span className="admin-muted">—</span>
      ) : (
        <span className="admin-badge-list">
          {user.roles.map((role) => (
            <span key={role.id} className="admin-badge admin-badge-role">
              {role.name}
            </span>
          ))}
        </span>
      ),
    },
    {
      key: 'createdAt',
      label: 'Created',
      sortType: 'date',
      sortValue: (user) => user.createdAt,
      flex: 0.85,
      minWidth: 110,
      render: (user) => new Date(user.createdAt).toLocaleDateString(),
    },
    {
      key: 'modifiedDate',
      label: 'Modified',
      sortType: 'date',
      sortValue: (user) => user.modifiedDate,
      flex: 0.85,
      minWidth: 110,
      render: (user) => new Date(user.modifiedDate).toLocaleDateString(),
    },
    {
      key: 'actions',
      label: 'Actions',
      fixedWidth: 380,
      sortable: false,
      resizable: false,
      align: 'right',
      render: (user) => (
        <div className="admin-table-actions">
          <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'edit', user })}>
            Edit
          </button>
          {canManageRoles && (
            <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'roles', user })}>
              Roles
            </button>
          )}
          <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'perms', user })}>
            Permissions
          </button>
          <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'geo', user })}>
            Geographic Access
          </button>
          <button
            type="button"
            className="admin-btn admin-btn-sm admin-btn-danger"
            disabled={user.id === userId}
            title={user.id === userId ? 'You cannot delete your own account.' : undefined}
            onClick={() => setModal({ type: 'delete', user })}
          >
            Delete
          </button>
        </div>
      ),
    },
  ], [canManageRoles, userId])

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
          <AdminTable
            columns={userColumns}
            rows={users}
            getRowKey={(user) => user.id}
            defaultSortKey="createdAt"
            defaultSortDir="desc"
          />
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
      {modal?.type === 'geo' && (
        <GeoAuthModal
          kind="user"
          targetId={modal.user.id}
          targetLabel={modal.user.username}
          onClose={() => setModal(null)}
          onSuccess={closeAnd}
        />
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
