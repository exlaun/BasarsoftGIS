import { Navigate, NavLink, Outlet, useLocation, useNavigate } from 'react-router-dom'
import { useAuth } from '../../context/auth-context'
import ThemeToggle from '../../components/ThemeToggle'
import SessionTimer from '../../components/SessionTimer'
import '../MapPage.css' // reuse the map top-bar styles so this header is identical to the map page's
import './Admin.css'

// Standalone full-screen admin shell: the same top bar as the map page (Logout + session timer on the
// left, title centered, theme toggle + "Return to map page" on the right), a left vertical navbar, and a
// content area that renders the active admin page via <Outlet />.
// Each section names the manage_* permission its API demands; links the caller lacks are hidden
// (the API enforces the same rule with per-resource policies, this just spares them the 403s).
const NAV = [
  { to: '/admin', end: true, label: 'Users', permission: 'manage_users' },
  { to: '/admin/roles', end: false, label: 'Roles', permission: 'manage_roles' },
  { to: '/admin/permissions', end: false, label: 'Permissions', permission: 'manage_permissions' },
]

// The mentor's "POI Yönetimi" menu: its own titled section under the RBAC links.
const POI_NAV = [
  { to: '/admin/pois', end: false, label: 'POIs', permission: 'manage_pois' },
  { to: '/admin/poi-categories', end: false, label: 'POI Categories', permission: 'manage_pois' },
]

const TRANSPORT_NAV = [
  {
    to: '/admin/transportation',
    end: false,
    label: 'Routes & Stops',
    permission: 'manage_transport_admin',
  },
]

export default function AdminLayout() {
  const { logout, permissions } = useAuth()
  const navigate = useNavigate()
  const location = useLocation()

  const granted = new Set(permissions)
  const nav = NAV.filter((item) => granted.has(item.permission))
  const poiNav = POI_NAV.filter((item) => granted.has(item.permission))
  const transportNav = TRANSPORT_NAV.filter((item) => granted.has(item.permission))

  // The /admin index is Users, so e.g. a manage_pois-only admin would land on a section they can't
  // use. Bounce them to their first permitted section instead. (AdminRoute already guarantees the
  // profile is loaded and that at least one manage_* permission exists.)
  const allowed = [...nav, ...poiNav, ...transportNav]
  const current = [...NAV, ...POI_NAV, ...TRANSPORT_NAV].find((item) =>
    item.end ? location.pathname === item.to : location.pathname.startsWith(item.to),
  )
  if (allowed.length > 0 && current && !granted.has(current.permission)) {
    return <Navigate to={allowed[0].to} replace />
  }

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
        <span className="map-title">BasarsoftGIS · Turkey Explorer</span>
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
            {nav.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) => `admin-nav-link${isActive ? ' is-active' : ''}`}
              >
                {item.label}
              </NavLink>
            ))}

            {poiNav.length > 0 && <div className="admin-nav-section">POI Management</div>}
            {poiNav.map((item) => (
              <NavLink
                key={item.to}
                to={item.to}
                end={item.end}
                className={({ isActive }) => `admin-nav-link${isActive ? ' is-active' : ''}`}
              >
                {item.label}
              </NavLink>
            ))}

            {transportNav.length > 0 && <div className="admin-nav-section">Transportation</div>}
            {transportNav.map((item) => (
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
