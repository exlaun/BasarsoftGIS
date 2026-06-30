import { useEffect, useState } from 'react'
import { useAuth } from '../context/auth-context'

// Remaining milliseconds until the (absolute) expiry timestamp, never negative.
function remainingMs(expiresAt) {
  if (!expiresAt) return null
  return Math.max(0, new Date(expiresAt).getTime() - Date.now())
}

// ms -> "MM:SS", zero-padded.
function formatMmSs(ms) {
  const totalSeconds = Math.floor(ms / 1000)
  const minutes = Math.floor(totalSeconds / 60)
  const seconds = totalSeconds % 60
  return `${String(minutes).padStart(2, '0')}:${String(seconds).padStart(2, '0')}`
}

// Plain MM:SS countdown to session auto-logout. Auto-logout itself is handled by
// AuthContext; this only displays the time left, recomputing from the absolute
// expiry each tick so setInterval drift never accumulates.
export default function SessionTimer() {
  const { expiresAt } = useAuth()
  const [left, setLeft] = useState(() => remainingMs(expiresAt))
  const [trackedExpiry, setTrackedExpiry] = useState(expiresAt)

  // Resync immediately (during render, not in an effect) when the expiry changes
  // on login/refresh, so the displayed time is never stale for a tick.
  if (expiresAt !== trackedExpiry) {
    setTrackedExpiry(expiresAt)
    setLeft(remainingMs(expiresAt))
  }

  useEffect(() => {
    if (!expiresAt) return undefined
    const id = setInterval(() => setLeft(remainingMs(expiresAt)), 1000)
    return () => clearInterval(id)
  }, [expiresAt])

  if (left == null) return null

  return <span className="map-timer">⏱ {formatMmSs(left)}</span>
}
