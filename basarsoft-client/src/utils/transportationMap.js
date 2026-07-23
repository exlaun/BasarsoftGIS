import Feature from 'ol/Feature.js'
import Point from 'ol/geom/Point.js'
import WKT from 'ol/format/WKT.js'
import { fromLonLat } from 'ol/proj.js'

const format = new WKT()
const DATA_PROJECTION = 'EPSG:4326'
const MAP_PROJECTION = 'EPSG:3857'

export function isRouteVisible(visibility, routeId) {
  return visibility?.[routeId] !== false
}

export function reconcileRouteVisibility(current, routes) {
  return Object.fromEntries(routes.map((route) => [route.id, isRouteVisible(current, route.id)]))
}

export function upsertRoute(routes, updated) {
  const index = routes.findIndex((route) => route.id === updated.id)
  if (index < 0) return [...routes, updated]
  const next = [...routes]
  next[index] = updated
  return next
}

export function parseRouteFeature(route, visibility = {}) {
  if (!route?.geometryWkt) return null
  try {
    const feature = format.readFeature(route.geometryWkt, {
      dataProjection: DATA_PROJECTION,
      featureProjection: MAP_PROJECTION,
    })
    if (feature.getGeometry()?.getType() !== 'LineString') return null
    feature.set('routeId', route.id)
    feature.set('apiType', 'route')
    feature.set('dbId', route.id)
    feature.set('routeColor', route.color)
    feature.set('color', route.color)
    feature.set('name', route.name)
    feature.set('transportVisible', isRouteVisible(visibility, route.id))
    return feature
  } catch {
    return null
  }
}

// Sample arrows approximately every kilometre, with one centered arrow for short routes and a hard
// cap for long/intercity routes. Bearings use a small local tangent so curves follow the road rather
// than the route's overall start/end direction.
export function generateDirectionArrows(lineFeature, { minimumSpacing = 1000, maximumArrows = 50 } = {}) {
  const geometry = lineFeature?.getGeometry()
  if (!geometry || geometry.getType() !== 'LineString') return []
  const length = geometry.getLength()
  if (!Number.isFinite(length) || length <= 0) return []

  const spacing = Math.max(minimumSpacing, length / maximumArrows)
  const count = Math.max(1, Math.min(maximumArrows, Math.floor(length / spacing)))
  const arrows = []
  for (let index = 0; index < count; index += 1) {
    const fraction = (index + 0.5) / count
    const tangentFraction = Math.min(0.02, Math.max(0.000001, 25 / length))
    const before = geometry.getCoordinateAt(Math.max(0, fraction - tangentFraction))
    const after = geometry.getCoordinateAt(Math.min(1, fraction + tangentFraction))
    const rotation = -Math.atan2(after[1] - before[1], after[0] - before[0])
    const arrow = new Feature(new Point(geometry.getCoordinateAt(fraction)))
    arrow.set('routeId', lineFeature.get('routeId'))
    arrow.set('routeColor', lineFeature.get('routeColor'))
    arrow.set('rotation', rotation)
    arrow.set('transportVisible', lineFeature.get('transportVisible') !== false)
    arrow.set('isDirectionArrow', true)
    arrows.push(arrow)
  }
  return arrows
}

export function syncRouteOverlaySources({ routes, visibility, lineSource, arrowSource }) {
  if (!lineSource || !arrowSource) return
  const lines = routes.map((route) => parseRouteFeature(route, visibility)).filter(Boolean)
  lineSource.clear()
  arrowSource.clear()
  lineSource.addFeatures(lines)
  arrowSource.addFeatures(lines.flatMap((line) => generateDirectionArrows(line)))
}

export function syncStopRoutePresentation(stopSource, routes, visibility) {
  if (!stopSource) return
  const routesById = new Map(routes.map((route) => [route.id, route]))
  for (const feature of stopSource.getFeatures()) {
    const routeId = feature.get('routeId')
    const route = routesById.get(routeId)
    if (route) {
      feature.set('routeName', route.name)
      feature.set('routeColor', route.color)
      // Only re-inherit for stops that never chose their own color. Overwriting unconditionally would
      // silently wipe a per-stop color every time its route was renamed or recolored.
      feature.set('color', feature.get('stopColor') || route.color || '#2563eb')
    }
    feature.set('transportVisible', isRouteVisible(visibility, routeId))
    feature.changed()
  }
}

export function applyRouteVisibility(routeId, visible, ...sources) {
  for (const source of sources) {
    if (!source) continue
    for (const feature of source.getFeatures()) {
      if (feature.get('routeId') === routeId) {
        feature.set('transportVisible', visible)
        feature.changed()
      }
    }
  }
}

export function upsertVehicleFeature(source, simulation, route, visibility = {}) {
  if (!source || !simulation) return null
  const featureId = `vehicle-${simulation.routeId}`
  const existing = source.getFeatureById(featureId)
  if (
    simulation.status === 'NotStarted' ||
    !Number.isFinite(simulation.longitude) ||
    !Number.isFinite(simulation.latitude)
  ) {
    if (existing) source.removeFeature(existing)
    return null
  }

  const coordinate = fromLonLat([simulation.longitude, simulation.latitude])
  const feature = existing ?? new Feature(new Point(coordinate))
  if (existing) existing.getGeometry().setCoordinates(coordinate)
  else {
    feature.setId(featureId)
    source.addFeature(feature)
  }
  feature.set('apiType', 'vehicle')
  feature.set('dbId', simulation.routeId)
  feature.set('routeId', simulation.routeId)
  feature.set('name', `Vehicle · ${route?.name ?? `Route ${simulation.routeId}`}`)
  feature.set('routeName', route?.name)
  feature.set('simulationStatus', simulation.status)
  feature.set('progressPercent', simulation.progressPercent)
  feature.set('transportVisible', isRouteVisible(visibility, simulation.routeId))
  feature.changed()
  return feature
}

export function removeVehicleFeature(source, routeId) {
  const feature = source?.getFeatureById(`vehicle-${routeId}`)
  if (feature) source.removeFeature(feature)
}

export function syncVehicleRoutePresentation(source, routes, visibility = {}) {
  if (!source) return
  const routesById = new Map(routes.map((route) => [route.id, route]))
  for (const feature of source.getFeatures()) {
    const routeId = feature.get('routeId')
    const route = routesById.get(routeId)
    if (!route) {
      source.removeFeature(feature)
      continue
    }
    feature.set('routeName', route.name)
    feature.set('name', `Vehicle · ${route.name}`)
    feature.set('transportVisible', isRouteVisible(visibility, routeId))
    feature.changed()
  }
}

export function persistedRoutingMutation(error, persistedFlag) {
  const data = error?.response?.data
  return data?.[persistedFlag] === true ? data : null
}
