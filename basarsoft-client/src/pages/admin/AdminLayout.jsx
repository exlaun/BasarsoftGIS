import { NavLink, Outlet, useNavigate } from 'react-router-dom'
import { useAuth } from '../../context/auth-context'
import ThemeToggle from '../../components/ThemeToggle'
import SessionTimer from '../../components/SessionTimer'
import '../MapPage.css' // reuse the map top-bar styles so this header is identical to the map page's
import './Admin.css'

// Standalone full-screen admin shell: the same top bar as the map page (Logout + session timer on the
// left, title centered, theme toggle + "Return to map page" on the right), a left vertical navbar, and a
// content area that renders the active admin page via <Outlet />.
const NAV = [
  { to: '/admin', end: true, label: 'Users' },
  { to: '/admin/roles', end: false, label: 'Roles' },
  { to: '/admin/permissions', end: false, label: 'Permissions' },
]

export default function AdminLayout() {
  const { logout } = useAuth()
  const navigate = useNavigate()

  return (
    <div className="admin">
      {/* Mirrors the map page's top bar; only the map's "Admin Panel" button is swapped for a
          "Return to map page" button, since this is already the admin panel. */}
      <header className="map-bar">
        <div className="map-bar-left">
          <button className="map-logout" type="button" onClick={logout}>
            Logout
          </button>
          <SessionTimer />
        </div>
        <span className="map-title">BasarsoftInternshipTask v0.1.5</span>
        <div className="map-bar-right">
          <ThemeToggle />
          <span className="map-bar-divider" aria-hidden="true" />
          <button className="map-logout" type="button" onClick={() => navigate('/map')}>
            Return to map page
          </button>
        </div>
      </header>

      <div className="admin-body">
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
        </nav>

        <main className="admin-main">
          <Outlet />
        </main>
      </div>
    </div>
  )
}
