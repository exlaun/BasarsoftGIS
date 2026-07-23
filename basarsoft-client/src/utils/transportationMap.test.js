import test from 'node:test'
import assert from 'node:assert/strict'
import Feature from 'ol/Feature.js'
import LineString from 'ol/geom/LineString.js'
import Point from 'ol/geom/Point.js'
import VectorSource from 'ol/source/Vector.js'
import {
  applyRouteVisibility,
  generateDirectionArrows,
  isRouteVisible,
  parseRouteFeature,
  persistedRoutingMutation,
  reconcileRouteVisibility,
  syncStopRoutePresentation,
  syncRouteOverlaySources,
  syncVehicleRoutePresentation,
  upsertVehicleFeature,
  removeVehicleFeature,
  upsertRoute,
} from './transportationMap.js'

test('route WKT parses as a projected LineString and invalid geometry is ignored', () => {
  const feature = parseRouteFeature({
    id: 7,
    name: 'Route 7',
    color: '#ff0000',
    geometryWkt: 'LINESTRING(29 41, 29.1 41.1)',
  })
  assert.equal(feature.getGeometry().getType(), 'LineString')
  assert.equal(feature.get('routeId'), 7)
  assert.equal(feature.get('apiType'), 'route')
  assert.equal(feature.get('dbId'), 7)
  assert.equal(feature.get('routeColor'), '#ff0000')
  assert.equal(parseRouteFeature({ id: 8, geometryWkt: 'POINT(29 41)' }), null)
  assert.equal(parseRouteFeature({ id: 9, geometryWkt: 'broken' }), null)
  assert.equal(parseRouteFeature({ id: 10, geometryWkt: null }), null)
})

test('vehicle updates reuse one feature and respect route visibility', () => {
  const source = new VectorSource()
  const route = { id: 4, name: 'Live route' }
  const first = upsertVehicleFeature(source, {
    routeId: 4,
    status: 'Running',
    longitude: 29,
    latitude: 41,
    progressPercent: 10,
  }, route, {})
  const originalCoordinates = first.getGeometry().getCoordinates()

  const second = upsertVehicleFeature(source, {
    routeId: 4,
    status: 'Running',
    longitude: 30,
    latitude: 42,
    progressPercent: 50,
  }, route, { 4: false })

  assert.equal(source.getFeatures().length, 1)
  assert.equal(second, first)
  assert.notDeepEqual(second.getGeometry().getCoordinates(), originalCoordinates)
  assert.equal(second.get('progressPercent'), 50)
  assert.equal(second.get('transportVisible'), false)

  syncVehicleRoutePresentation(source, [{ id: 4, name: 'Renamed route' }], { 4: true })
  assert.equal(second.get('routeName'), 'Renamed route')
  assert.equal(second.get('transportVisible'), true)

  removeVehicleFeature(source, 4)
  assert.equal(source.getFeatures().length, 0)
})

test('NotStarted simulation removes an existing vehicle instead of duplicating it', () => {
  const source = new VectorSource()
  upsertVehicleFeature(source, {
    routeId: 8,
    status: 'Completed',
    longitude: 29,
    latitude: 41,
    progressPercent: 100,
  }, { id: 8, name: 'Done' })
  assert.equal(source.getFeatures().length, 1)

  upsertVehicleFeature(source, { routeId: 8, status: 'NotStarted' }, { id: 8 })
  assert.equal(source.getFeatures().length, 0)
})

test('new routes default visible while explicit visibility is preserved', () => {
  assert.equal(isRouteVisible({}, 1), true)
  assert.deepEqual(
    reconcileRouteVisibility({ 1: false }, [{ id: 1 }, { id: 2 }]),
    { 1: false, 2: true },
  )
})

test('visibility updates route lines, arrows, and stops together', () => {
  const line = new Feature(new LineString([[0, 0], [2000, 0]]))
  const arrow = new Feature(new Point([1000, 0]))
  const stop = new Feature(new Point([0, 0]))
  for (const feature of [line, arrow, stop]) feature.set('routeId', 4)
  const sources = [new VectorSource({ features: [line] }), new VectorSource({ features: [arrow] }), new VectorSource({ features: [stop] })]

  applyRouteVisibility(4, false, ...sources)

  assert.ok(sources.every((source) => source.getFeatures()[0].get('transportVisible') === false))
})

test('direction arrows follow line direction and reverse their rotation', () => {
  const forward = new Feature(new LineString([[0, 0], [3000, 0]]))
  const reverse = new Feature(new LineString([[3000, 0], [0, 0]]))
  const forwardArrows = generateDirectionArrows(forward)
  const reverseArrows = generateDirectionArrows(reverse)

  assert.equal(forwardArrows.length, 3)
  assert.equal(reverseArrows.length, 3)
  const difference = Math.abs(forwardArrows[0].get('rotation') - reverseArrows[0].get('rotation'))
  assert.ok(Math.abs(difference - Math.PI) < 0.0001)
})

test('route overlay synchronization replaces old geometry and arrows after rebuild', () => {
  const lines = new VectorSource()
  const arrows = new VectorSource()
  const route = { id: 1, name: 'A', color: '#00aa00', geometryWkt: 'LINESTRING(29 41,29.01 41.01)' }
  syncRouteOverlaySources({ routes: [route], visibility: {}, lineSource: lines, arrowSource: arrows })
  const before = lines.getFeatures()[0].getGeometry().getLastCoordinate()

  syncRouteOverlaySources({
    routes: [{ ...route, geometryWkt: 'LINESTRING(29 41,30 42)' }],
    visibility: {},
    lineSource: lines,
    arrowSource: arrows,
  })

  assert.equal(lines.getFeatures().length, 1)
  assert.notDeepEqual(lines.getFeatures()[0].getGeometry().getLastCoordinate(), before)
  assert.ok(arrows.getFeatures().length > 0)
})

test('a route dropped from the list loses its line and its arrows', () => {
  const lines = new VectorSource()
  const arrows = new VectorSource()
  const kept = { id: 1, name: 'Kept', color: '#00aa00', geometryWkt: 'LINESTRING(29 41,29.1 41.1)' }
  const deleted = { id: 2, name: 'Deleted', color: '#aa0000', geometryWkt: 'LINESTRING(30 42,30.1 42.1)' }
  syncRouteOverlaySources({ routes: [kept, deleted], visibility: {}, lineSource: lines, arrowSource: arrows })
  assert.equal(lines.getFeatures().length, 2)

  // Deletion is just the route leaving the list — MapPage never touches these sources directly.
  syncRouteOverlaySources({ routes: [kept], visibility: {}, lineSource: lines, arrowSource: arrows })

  assert.deepEqual(lines.getFeatures().map((feature) => feature.get('routeId')), [1])
  assert.ok(arrows.getFeatures().length > 0)
  assert.ok(arrows.getFeatures().every((feature) => feature.get('routeId') === 1))
})

test('state-carrying routing errors are recognized without treating ordinary errors as committed', () => {
  const partial = { response: { data: { orderPersisted: true, route: { id: 1 }, stops: [] } } }
  assert.equal(persistedRoutingMutation(partial, 'orderPersisted').route.id, 1)
  assert.equal(persistedRoutingMutation({ response: { data: {} } }, 'orderPersisted'), null)

  const relocated = {
    response: {
      data: {
        locationPersisted: true,
        stop: { id: 9, wkt: 'POINT (30 40)' },
        route: { id: 1, isGeometryStale: true },
      },
    },
  }
  assert.equal(persistedRoutingMutation(relocated, 'locationPersisted').stop.id, 9)

  const reconciled = upsertRoute(
    [{ id: 1, isGeometryStale: false, geometryWkt: 'LINESTRING(0 0,1 1)' }],
    { ...partial.response.data.route, isGeometryStale: true, geometryWkt: 'LINESTRING(0 0,1 1)' },
  )
  assert.equal(reconciled[0].isGeometryStale, true)
  assert.equal(reconciled[0].geometryWkt, 'LINESTRING(0 0,1 1)')
})

test('route presentation refresh updates stop metadata without rebuilding stop geometry', () => {
  const stop = new Feature(new Point([100, 200]))
  stop.set('routeId', 3)
  const source = new VectorSource({ features: [stop] })

  syncStopRoutePresentation(
    source,
    [{ id: 3, name: 'Renamed', color: '#123456' }],
    { 3: false },
  )

  assert.equal(stop.get('routeName'), 'Renamed')
  assert.equal(stop.get('routeColor'), '#123456')
  assert.equal(stop.get('transportVisible'), false)
  assert.deepEqual(stop.getGeometry().getCoordinates(), [100, 200])
})

test('a recolored route re-tints inheriting stops but leaves per-stop colors alone', () => {
  const inheriting = new Feature(new Point([0, 0]))
  inheriting.set('routeId', 3)
  inheriting.set('stopColor', null)
  inheriting.set('color', '#111111')

  const overridden = new Feature(new Point([1, 1]))
  overridden.set('routeId', 3)
  overridden.set('stopColor', '#00ff00')
  overridden.set('color', '#00ff00')

  const source = new VectorSource({ features: [inheriting, overridden] })
  syncStopRoutePresentation(source, [{ id: 3, name: 'Route 3', color: '#123456' }], {})

  assert.equal(inheriting.get('color'), '#123456')
  assert.equal(overridden.get('color'), '#00ff00')
  // Both still report the route's color — the override changes the marker tint, not which route
  // the stop belongs to.
  assert.equal(overridden.get('routeColor'), '#123456')
})
