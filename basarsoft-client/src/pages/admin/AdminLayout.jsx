import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../../context/auth-context'
import ThemeToggle from '../../components/ThemeToggle'
import './Admin.css'

// Standalone full-screen admin shell: a left vertical navbar + a content area that renders the active
// admin page via <Outlet />. No map or map top-bar here — this is a separate interface reached from the
// map's "Admin Panel" button.
const NAV = [
  { to: '/admin', end: true, label: 'Users' },
  { to: '/admin/roles', end: false, label: 'Roles' },
  { to: '/admin/permissions', end: false, label: 'Permissions' },
]

export default function AdminLayout() {
  const { username, logout } = useAuth()
  const navigate = useNavigate()

  return (
    <div className="admin">
      <nav className="admin-nav" aria-label="Admin navigation">
        <div className="admin-nav-brand">
          Admin Panel
          <small>Admin</small>
        </div>

        <div className="admin-nav-links">
          {NAV.map((item) => (
            <NavLink
              key={item.to}
              to={item.to}
              end={item.end}
              className={({ isActive }) => `admin-nav-link${isActive ? ' is-active' : ''}`}
            >
              {item.label}
            </NavLink>
          ))}
        </div>

        <div className="admin-nav-foot">
          <div className="admin-nav-user">Signed in as {username}</div>
          <button type="button" className="admin-btn admin-btn-sm" onClick={() => navigate('/map')}>
            ← Back to map
          </button>
          <div className="admin-nav-row">
            <ThemeToggle />
            <button type="button" className="admin-btn admin-btn-sm" onClick={logout}>
              Logout
            </button>
          </div>
        </div>
      </nav>

      <main className="admin-main">
        <Outlet />
      </main>
    </div>
  )
}
