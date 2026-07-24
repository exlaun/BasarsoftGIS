export const NOT_STARTED_SIMULATION = Object.freeze({
  runId: null,
  status: 'NotStarted',
  progressPercent: 0,
  sequence: 0,
})

export function simulationForRoute(states, routeId) {
  return states?.[routeId] ?? { ...NOT_STARTED_SIMULATION, routeId }
}

// Reject stale messages within one run and late messages from an older run. NotStarted has no run id
// and is intentionally authoritative: it is how a reconnected client learns the API restarted.
export function reconcileSimulationState(current, incoming) {
  if (!incoming) return current
  if (!current || incoming.status === 'NotStarted') return incoming
  if (current.status === 'NotStarted') return incoming

  if (current.runId === incoming.runId) {
    return Number(incoming.sequence ?? 0) >= Number(current.sequence ?? 0) ? incoming : current
  }

  const currentStarted = Date.parse(current.startedAt ?? '')
  const incomingStarted = Date.parse(incoming.startedAt ?? '')
  if (Number.isFinite(currentStarted) && Number.isFinite(incomingStarted)) {
    return incomingStarted >= currentStarted ? incoming : current
  }
  return incoming
}

// A route that has never started exposes only Start. A live run exposes Stop + End; once paused, Stop
// becomes Resume. End clears the run so the route falls back to first-time Start. This state-derived
// list is shared by the route panel and popup, so stale actions never remain visible.
export function simulationControls({ simulation, canControl, cameraFollowed, route }) {
  const status = simulation?.status ?? 'NotStarted'
  const ready = Boolean(route?.stopCount >= 2 && route?.geometryWkt && !route?.isGeometryStale)
  const actions = []

  if (canControl) {
    if (status === 'NotStarted') {
      actions.push({ action: 'start', label: 'Start Simulation', disabled: !ready, variant: 'primary' })
    } else if (status === 'Running') {
      actions.push({ action: 'stop', label: 'Stop Simulation', disabled: false, variant: 'default' })
      actions.push({ action: 'end', label: 'End Simulation', disabled: false, variant: 'danger' })
    } else if (status === 'Stopped') {
      actions.push({ action: 'resume', label: 'Resume Simulation', disabled: false, variant: 'primary' })
      actions.push({ action: 'end', label: 'End Simulation', disabled: false, variant: 'danger' })
    } else {
      // Completed / Failed runs remain inspectable until End clears their marker and state.
      actions.push({ action: 'end', label: 'End Simulation', disabled: false, variant: 'danger' })
    }
  }

  return {
    status,
    statusLabel: status === 'NotStarted' ? 'Not started' : status,
    ready,
    actions,
    showFollow: status !== 'NotStarted',
    followAction: cameraFollowed ? 'unfollow' : 'follow',
    followLabel: cameraFollowed ? 'Stop Following' : 'Follow',
    followDisabled: !cameraFollowed && status === 'NotStarted',
  }
}

export function formatSimulationProgress(value) {
  const progress = Number(value)
  return `${Number.isFinite(progress) ? Math.max(0, Math.min(100, progress)).toFixed(1) : '0.0'}%`
}

// End used to surface every failure as the same generic toast, which hid the common development
// failure mode: a stale API process that predates the endpoint and returns 404. Keep request failures
// server-authoritative, but translate the response into something the user can act on.
export function endSimulationErrorMessage(error) {
  const status = error?.response?.status
  const serverMessage = error?.response?.data?.message

  if (status === 404) {
    return 'The running API does not include End Simulation. Restart or redeploy the API, then try again.'
  }
  if (status === 403) {
    return 'You no longer have permission to end route simulations.'
  }
  if (!error?.response) {
    return 'Could not reach the API. Check that it is running, then try again.'
  }
  if (status >= 500) {
    return serverMessage || 'The API could not end the simulation. Check the server logs and try again.'
  }
  return serverMessage || 'Could not end the simulation.'
}
