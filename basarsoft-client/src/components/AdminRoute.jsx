import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../context/auth-context'

// Gate for the /admin section: must be authenticated AND hold admin access. While the RBAC profile is
// still loading (isAdmin not yet known) we render nothing rather than bounce a real admin who hard-
// refreshed on an /admin URL. Non-admins are sent back to the map.
export default function AdminRoute() {
  const { isAuthenticated, isAdmin, profileLoading } = useAuth()

  if (!isAuthenticated) return <Navigate to="/login" replace />
  if (profileLoading) return null
  if (!isAdmin) return <Navigate to="/map" replace />
  return <Outlet />
}
