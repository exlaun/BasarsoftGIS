const clampPercent = (value) => {
  const number = Number(value)
  if (!Number.isFinite(number)) return 0
  return Math.max(0, Math.min(100, number))
}

const stopKey = (stop) => stop?.id ?? stop?.sequenceOrder

const uniqueOrderedStops = (stops) => {
  const seen = new Set()
  return [...(stops ?? [])]
    .filter((stop) => stop && stopKey(stop) != null)
    .sort((left, right) => Number(left.sequenceOrder ?? 0) - Number(right.sequenceOrder ?? 0))
    .filter((stop) => {
      const key = stopKey(stop)
      if (seen.has(key)) return false
      seen.add(key)
      return true
    })
}

const addEntry = (entries, entry) => {
  if (!entry) return
  if (!entry.stop && entry.type !== 'moving' && entry.type !== 'stopped') return
  const key = entry.stop ? `stop:${stopKey(entry.stop)}` : `state:${entry.type}`
  if (entries.some((candidate) => candidate.key === key)) return
  entries.push({ ...entry, key })
}

// Builds the compact route context shown by the live simulation card. The server's currentStopIndex
// is the stop nearest to the vehicle's traveled route distance; keep that stop separate from the
// moving/stopped marker so the summary can name it without pretending the vehicle is stopped there.
export function simulationStopContext(stops, simulation) {
  const orderedStops = uniqueOrderedStops(stops)
  if (orderedStops.length === 0) {
    return {
      entries: [],
      orderedStops,
      nearestStop: null,
      centerStop: null,
      isMoving: false,
    }
  }

  const progressPercent = clampPercent(simulation?.progressPercent)
  const rawIndex = Number(simulation?.currentStopIndex)
  const nearestIndex = Number.isInteger(rawIndex)
    ? Math.max(0, Math.min(orderedStops.length - 1, rawIndex))
    : progressPercent >= 100 ? orderedStops.length - 1 : 0
  const atEndpoint = progressPercent <= 0 || progressPercent >= 100
  const isMoving = simulation?.status === 'Running' && !atEndpoint
  const isStopped = simulation?.status === 'Stopped'
  const nearestStop = orderedStops[nearestIndex]
  const centerStop = isMoving ? null : nearestStop
  const previousStop = nearestIndex > 0 ? orderedStops[nearestIndex - 1] : null
  const upcomingStop = nearestIndex < orderedStops.length - 1 ? orderedStops[nearestIndex + 1] : null
  const entries = []

  addEntry(entries, { type: 'first', label: 'First', stop: orderedStops[0] })
  addEntry(entries, { type: 'previous', label: 'Previous', stop: previousStop })
  if (isMoving) addEntry(entries, { type: 'moving', label: 'Moving' })
  else if (isStopped) addEntry(entries, { type: 'stopped', label: 'Stopped' })
  else addEntry(entries, { type: 'current', label: 'Current', stop: centerStop })
  addEntry(entries, { type: 'upcoming', label: 'Upcoming', stop: upcomingStop })
  addEntry(entries, { type: 'last', label: 'Last', stop: orderedStops.at(-1) })

  return {
    entries,
    orderedStops,
    nearestStop,
    centerStop,
    previousStop,
    upcomingStop,
    isMoving,
    isStopped,
  }
}

export function simulationProgressMetrics(route, simulation) {
  const completedPercent = clampPercent(simulation?.progressPercent)
  const remainingPercent = 100 - completedPercent
  const totalDistanceMeters = Number(route?.distanceMeters)
  const completedDistanceMeters = Number.isFinite(totalDistanceMeters)
    ? totalDistanceMeters * completedPercent / 100
    : null
  const remainingDistanceMeters = Number.isFinite(totalDistanceMeters)
    ? totalDistanceMeters - completedDistanceMeters
    : null

  return {
    completedPercent,
    remainingPercent,
    completedDistanceMeters,
    remainingDistanceMeters,
  }
}
