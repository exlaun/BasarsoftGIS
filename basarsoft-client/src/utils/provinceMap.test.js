import assert from 'node:assert/strict'
import test from 'node:test'
import {
  DEFAULT_PROVINCE_COLOR,
  isProvinceFeatureHighlighted,
  parseProvinceMapItem,
  parseProvinceMapItems,
  selectedProvinceIdFromFeatures,
} from './provinceMap.js'

test('province map parser creates a linked, same-color boundary and capital pair', () => {
  const pair = parseProvinceMapItem({
    id: 34,
    name: 'İstanbul',
    capitalName: 'İstanbul',
    color: '#2563EB',
    boundaryWkt: 'MULTIPOLYGON(((28 40,30 40,30 42,28 42,28 40)))',
    capitalWkt: 'POINT(28.9784 41.0082)',
  })

  assert.equal(pair.boundary.getGeometry().getType(), 'MultiPolygon')
  assert.equal(pair.capital.getGeometry().getType(), 'Point')
  assert.equal(pair.boundary.get('provinceId'), 34)
  assert.equal(pair.capital.get('provinceId'), 34)
  assert.equal(pair.boundary.get('provinceColor'), '#2563EB')
  assert.equal(pair.capital.get('provinceColor'), '#2563EB')
  assert.equal(pair.capital.get('name'), 'İstanbul · İstanbul')
  assert.equal(isProvinceFeatureHighlighted(pair.boundary, 34, null), true)
  assert.equal(isProvinceFeatureHighlighted(pair.capital, null, 34), true)
  assert.equal(isProvinceFeatureHighlighted(pair.capital, 6, 35), false)
  assert.equal(selectedProvinceIdFromFeatures([pair.capital]), 34)
  assert.equal(selectedProvinceIdFromFeatures([pair.boundary]), 34)
})

test('province map parser rejects incomplete pairs and safely normalizes colors', () => {
  assert.equal(parseProvinceMapItem({
    id: 6,
    boundaryWkt: 'POINT(32 39)',
    capitalWkt: 'POINT(32 39)',
  }), null)

  const parsed = parseProvinceMapItems([
    {
      id: 6,
      name: 'Ankara',
      capitalName: 'Ankara',
      color: 'not-a-color',
      boundaryWkt: 'POLYGON((31 39,33 39,33 41,31 41,31 39))',
      capitalWkt: 'POINT(32.85 39.93)',
    },
    { id: 7, boundaryWkt: 'broken', capitalWkt: 'POINT(30 37)' },
  ])

  assert.equal(parsed.boundaries.length, 1)
  assert.equal(parsed.capitals.length, 1)
  assert.equal(parsed.boundaries[0].get('provinceColor'), DEFAULT_PROVINCE_COLOR)
})

test('province click resolution ignores unrelated features and clears on an empty hit', () => {
  const unrelated = { get: () => undefined }
  const capital = { get: (key) => (key === 'provinceId' ? 6 : undefined) }

  assert.equal(selectedProvinceIdFromFeatures([unrelated, capital]), 6)
  assert.equal(selectedProvinceIdFromFeatures([unrelated]), null)
  assert.equal(selectedProvinceIdFromFeatures([]), null)
})
