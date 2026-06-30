import { Navigate, Route, Routes } from 'react-router-dom'
import ProtectedRoute from './components/ProtectedRoute'
import LoginPage from './pages/LoginPage'
import MapPage from './pages/MapPage'

export default function App() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route element={<ProtectedRoute />}>
        <Route path="/map" element={<MapPage />} />
      </Route>
      {/* Anything else funnels to /map, which the guard redirects to /login if needed. */}
      <Route path="*" element={<Navigate to="/map" replace />} />
    </Routes>
  )
}
