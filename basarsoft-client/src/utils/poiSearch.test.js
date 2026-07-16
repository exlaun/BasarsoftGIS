import assert from 'node:assert/strict'
import test from 'node:test'
import { foldPoiSearchText, searchPoiFeatures } from './poiSearch.js'

const feature = (properties) => ({
  get: (name) => properties[name],
})

test('folds Turkish letters and accents for keyboard-independent matching', () => {
  assert.equal(foldPoiSearchText('İstanbul Nişantaşı'), 'istanbul nisantasi')
  assert.equal(foldPoiSearchText('ISTANBUL'), 'istanbul')
})

test('ranks name matches before category-path matches', () => {
  const categoryMatch = feature({ name: 'Merkez', categoryPath: 'Customer Service > Retail' })
  const nameMatch = feature({ name: 'Retail Point', categoryPath: 'Operations' })

  assert.deepEqual(searchPoiFeatures([categoryMatch, nameMatch], 'retail'), [nameMatch, categoryMatch])
})

test('limits results and recomputes from a newly supplied POI array', () => {
  const initial = [feature({ name: 'Depot 1', categoryPath: 'Operations' })]
  const refreshed = [...initial, feature({ name: 'Depot 2', categoryPath: 'Operations' })]

  assert.equal(searchPoiFeatures(initial, 'depot').length, 1)
  assert.equal(searchPoiFeatures(refreshed, 'depot').length, 2)
  assert.equal(searchPoiFeatures(refreshed, 'depot', 1).length, 1)
})
