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
  // RBAC context from GET /api/auth/me: { isAdmin, roles[], permissions[] }. Null until loaded.
  const [profile, setProfile] = useState(null)
  // True while /me is in flight for the current token, so guards can wait instead of wrongly
  // redirecting a would-be admin before their access is known (e.g. a hard refresh on /admin).
  const [profileLoading, setProfileLoading] = useState(() => Boolean(loadInitialAuth()?.token))

  const logout = useCallback(() => {
    localStorage.removeItem(STORAGE_KEY)
    setAuth(null)
    setProfile(null)
    setProfileLoading(false)
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

  // Load the caller's RBAC context whenever the token changes (login/register/refresh). Drives the
  // map's admin button and the /admin route guard. The loading flag is flipped ON in persist()/at init
  // (event/init time) and OFF here in the async callbacks, so the effect body itself never calls
  // setState synchronously. A failure just leaves the user as non-admin; a 401 is handled by the axios
  // client (which force-logs-out). No token -> nothing to load (logout already cleared the profile).
  useEffect(() => {
    if (!auth?.token) return undefined
    let cancelled = false
    client
      .get('/api/auth/me')
      .then(({ data }) => {
        if (!cancelled) setProfile(data)
      })
      .catch(() => {
        if (!cancelled) setProfile(null)
      })
      .finally(() => {
        if (!cancelled) setProfileLoading(false)
      })
    return () => {
      cancelled = true
    }
  }, [auth?.token])

  const persist = (data) => {
    // data = { token, username, expiresAt }
    localStorage.setItem(STORAGE_KEY, JSON.stringify(data))
    setProfileLoading(true) // about to (re)load /api/auth/me for the new token
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

  // Re-read the caller's current roles/permissions after an admin mutation. The JWT itself does not
  // carry RBAC data, so a profile refresh is enough to make permission changes affect the open session.
  const refreshProfile = useCallback(async () => {
    if (!auth?.token) return null
    setProfileLoading(true)
    try {
      const { data } = await client.get('/api/auth/me')
      setProfile(data)
      return data
    } catch {
      setProfile(null)
      return null
    } finally {
      setProfileLoading(false)
    }
  }, [auth?.token])

  const value = useMemo(
    () => ({
      username: auth?.username ?? null,
      expiresAt: auth?.expiresAt ?? null,
      isAuthenticated: Boolean(auth?.token),
      // RBAC context (from /api/auth/me). isAdmin gates the admin button + /admin route.
      isAdmin: profile?.isAdmin ?? false,
      roles: profile?.roles ?? [],
      permissions: profile?.permissions ?? [],
      profileLoading,
      login,
      register,
      forgotPassword,
      resetPassword,
      refreshProfile,
      logout,
    }),
    [auth, profile, profileLoading, login, register, forgotPassword, resetPassword, refreshProfile, logout],
  )

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>
}
