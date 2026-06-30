import { useCallback, useEffect, useMemo, useState } from 'react'
import client, { STORAGE_KEY, getStoredAuth } from '../api/client'
import { AuthContext } from './auth-context'

// Load the stored session, discarding it up front if it has already expired
// (avoids a flash of "logged in" on refresh with a dead token).
function loadInitialAuth() {
  const stored = getStoredAuth()
  if (stored?.expiresAt && new Date(stored.expiresAt).getTime() <= Date.now()) {
    localStorage.removeItem(STORAGE_KEY)
    return null
  }
  return stored
}

export function AuthProvider({ children }) {
  const [auth, setAuth] = useState(loadInitialAuth)

  const logout = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY)
    setAuth(null)
  }, [])

  // Core requirement: auto-logout the instant the token expires. Re-arm a timer
  // for the remaining lifetime whenever auth changes (login/register/refresh) so
  // an idle user on the map screen is still sent back to /login on expiry.
  useEffect(() => {
    if (!auth?.expiresAt) return undefined
    const msLeft = new Date(auth.expiresAt).getTime() - Date.now()
    const timer = setTimeout(logout, Math.max(0, msLeft))
    return () => clearTimeout(timer)
  }, [auth, logout])

  const persist = (data) => {
    // data = { token, username, expiresAt }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(data))
    setAuth(data)
  }

  const login = useCallback(async (username, password) => {
    const { data } = await client.post('/api/auth/login', { username, password })
    persist(data)
    return data
  }, [])

  const register = useCallback(async (username, password) => {
    const { data } = await client.post('/api/auth/register', { username, password })
    persist(data)
    return data
  }, [])

  // Forgot-password step 1: confirm the username exists. Throws (404) if it doesn't.
  // These two don't log the user in, so they never call persist().
  const forgotPassword = useCallback(async (username) => {
    const { data } = await client.post('/api/auth/forgot-password', { username })
    return data
  }, [])

  // Forgot-password step 2: set the new password for a confirmed username.
  const resetPassword = useCallback(async (username, newPassword) => {
    const { data } = await client.post('/api/auth/reset-password', { username, newPassword })
    return data
  }, [])

  const value = useMemo(
    () => ({
      username: auth?.username ?? null,
      expiresAt: auth?.expiresAt ?? null,
      isAuthenticated: Boolean(auth?.token),
      login,
      register,
      forgotPassword,
      resetPassword,
      logout,
    }),
    [auth, login, register, forgotPassword, resetPassword, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
