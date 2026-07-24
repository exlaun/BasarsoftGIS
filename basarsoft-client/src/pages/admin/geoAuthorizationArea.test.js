import test from 'node:test'
import assert from 'node:assert/strict'
import Feature from 'ol/Feature.js'
import WKT from 'ol/format/WKT.js'
import LineString from 'ol/geom/LineString.js'
import Polygon from 'ol/geom/Polygon.js'
import {
  combineAuthorizationAreaFeatures,
  readAuthorizationAreaFeatures,
  writeAuthorizationAreaWkt,
} from './geoAuthorizationArea.js'

const identityProjection = {
  dataProjection: 'EPSG:4326',
  featureProjection: 'EPSG:4326',
}

test('a disconnected MultiPolygon loads as independently editable components', () => {
  const features = readAuthorizationAreaFeatures(
    'MULTIPOLYGON (((29 40, 30 40, 30 41, 29 40)), ((35 37, 36 37, 36 38, 35 37)))',
    identityProjection,
  )

  assert.equal(features.length, 2)
  assert.ok(features.every((feature) => feature.getGeometry().getType() === 'Polygon'))

  features[1].getGeometry().translate(1, 0)
  const combined = combineAuthorizationAreaFeatures(features)
  assert.equal(combined.getType(), 'MultiPolygon')
  assert.equal(combined.getPolygons().length, 2)
  assert.equal(combined.getPolygons()[1].getFirstCoordinate()[0], 36)
})

test('saving one component still emits normalized MultiPolygon WKT', () => {
  const feature = new Feature(
    new Polygon([[
      [29, 40],
      [30, 40],
      [30, 41],
      [29, 40],
    ]]),
  )

  const serialized = writeAuthorizationAreaWkt([feature], identityProjection)
  const geometry = new WKT().readGeometry(serialized)

  assert.match(serialized, /^MULTIPOLYGON/)
  assert.equal(geometry.getType(), 'MultiPolygon')
  assert.equal(geometry.getPolygons().length, 1)
})

test('appending a disconnected component preserves all existing polygons', () => {
  const existing = readAuthorizationAreaFeatures(
    'MULTIPOLYGON (((26 39, 27 39, 27 40, 26 39)), ((32 39, 33 39, 33 40, 32 39)))',
    identityProjection,
  )
  const appended = new Feature(
    new Polygon([[
      [40, 37],
      [41, 37],
      [41, 38],
      [40, 37],
    ]]),
  )

  const serialized = writeAuthorizationAreaWkt([...existing, appended], identityProjection)
  const geometry = new WKT().readGeometry(serialized)

  assert.equal(geometry.getPolygons().length, 3)
  assert.deepEqual(geometry.getPolygons()[2].getFirstCoordinate(), [40, 37])
})

test('an empty map has no save payload and unsupported geometries are rejected', () => {
  assert.equal(writeAuthorizationAreaWkt([], identityProjection), null)
  assert.throws(
    () => combineAuthorizationAreaFeatures([new Feature(new LineString([[0, 0], [1, 1]]))]),
    /Polygon or MultiPolygon/,
  )
})
