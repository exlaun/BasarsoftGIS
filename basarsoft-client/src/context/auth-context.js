import { createContext, useContext } from 'react'

// Kept in a non-component module so Fast Refresh stays happy
// (AuthContext.jsx only exports the AuthProvider component).
export const AuthContext = createContext(null)

export function useAuth() {
  const ctx = useContext(AuthContext)
  if (!ctx) {
    throw new Error('useAuth must be used within an AuthProvider')
  }
  return ctx
}
