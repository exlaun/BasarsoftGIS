import { Navigate, Outlet } from 'react-router-dom'
import { useAuth } from '../context/auth-context'

// Gate for authenticated-only routes. When the token is cleared (logout or
// auto-logout on expiry), isAuthenticated flips false and we redirect to /login.
export default function ProtectedRoute() {
  const { isAuthenticated } = useAuth()
  return isAuthenticated ? <Outlet /> : <Navigate to="/login" replace />
}
