// Client-side smoothing for the live vehicle marker. The server publishes one authoritative position
// per tick (~1s); drawing those verbatim makes the car teleport in jumps. This eases the *progress*
// between server updates so MapPage can glide the marker continuously along the route line (positioned
// with the route geometry's getCoordinateAt). The server stays the source of truth: every new target
// wins immediately — we only interpolate the gap up to it, never past it.

const DEFAULT_DURATION_MS = 1000
const MIN_DURATION_MS = 150
const MAX_DURATION_MS = 3000
const TERMINAL = new Set(['Stopped', 'Completed', 'Failed', 'NotStarted'])

function clampProgress(value) {
  const n = Number(value)
  if (!Number.isFinite(n)) return 0
  return Math.max(0, Math.min(100, n))
}

export function createVehicleAnimator({ defaultDurationMs = DEFAULT_DURATION_MS } = {}) {
  // routeId -> { fromProgress, toProgress, startTime, durationMs, done, lastPushAt }
  const tweens = new Map()

  function easedProgress(routeId, now) {
    const tween = tweens.get(routeId)
    if (!tween) return 0
    if (tween.done || tween.durationMs <= 0) return tween.toProgress
    const elapsed = now - tween.startTime
    if (elapsed <= 0) return tween.fromProgress
    if (elapsed >= tween.durationMs) return tween.toProgress
    return tween.fromProgress + (tween.toProgress - tween.fromProgress) * (elapsed / tween.durationMs)
  }

  function pushTarget(routeId, { progressPercent, status } = {}, now) {
    const target = clampProgress(progressPercent)
    const terminal = TERMINAL.has(status)
    const existing = tweens.get(routeId)
    // Ease over roughly the observed gap between server updates (clamped). The first push for a route,
    // and any terminal state, snaps straight to the target instead of easing.
    const durationMs = existing && Number.isFinite(existing.lastPushAt)
      ? Math.max(MIN_DURATION_MS, Math.min(MAX_DURATION_MS, now - existing.lastPushAt))
      : defaultDurationMs
    const from = existing ? easedProgress(routeId, now) : target
    tweens.set(routeId, {
      fromProgress: terminal ? target : from,
      toProgress: target,
      startTime: now,
      durationMs: terminal ? 0 : durationMs,
      done: terminal,
      lastPushAt: now,
    })
  }

  function remove(routeId) {
    tweens.delete(routeId)
  }

  function has(routeId) {
    return tweens.has(routeId)
  }

  // Routes still easing toward a target. Terminal/snapped routes are excluded because they are placed
  // once on their push and need no per-frame work — so an idle map stops requesting animation frames.
  function animatingRouteIds() {
    const ids = []
    for (const [routeId, tween] of tweens) {
      if (!tween.done) ids.push(routeId)
    }
    return ids
  }

  return { pushTarget, easedProgress, remove, has, animatingRouteIds }
}
