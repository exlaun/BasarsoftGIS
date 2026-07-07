import { Navigate, Route, Routes } from 'react-router-dom'
import ProtectedRoute from './components/ProtectedRoute'
import AdminRoute from './components/AdminRoute'
import LoginPage from './pages/LoginPage'
import MapPage from './pages/MapPage'
import AdminLayout from './pages/admin/AdminLayout'
import UsersPage from './pages/admin/UsersPage'
import RolesPage from './pages/admin/RolesPage'
import PermissionsPage from './pages/admin/PermissionsPage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedRoute />}>
        <Route path="/map" element={<MapPage />} />
      </Route>
      {/* Admin is a separate full-screen section with its own left-navbar layout (no map chrome).
          The guard requires admin access; the nested routes render inside AdminLayout's <Outlet />. */}
      <Route element={<AdminRoute />}>
        <Route path="/admin" element={<AdminLayout />}>
          <Route index element={<UsersPage />} />
          <Route path="roles" element={<RolesPage />} />
          <Route path="permissions" element={<PermissionsPage />} />
        </Route>
      </Route>
      {/* Anything else funnels to /map, which the guard redirects to /login if needed. */}
      <Route path="*" element={<Navigate to="/map" replace />} />
    </Routes>
  )
}
