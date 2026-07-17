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
  const categoryMatch = feature({ name: 'Galata View', categoryPath: 'Culture & Tourism > Museum' })
  const nameMatch = feature({ name: 'Museum Cafe', categoryPath: 'Food & Drink > Cafe' })

  assert.deepEqual(searchPoiFeatures([categoryMatch, nameMatch], 'museum'), [nameMatch, categoryMatch])
})

test('limits results and recomputes from a newly supplied POI array', () => {
  const initial = [feature({ name: 'Explorer Hub 1', categoryPath: 'Culture & Tourism > Visitor Center' })]
  const refreshed = [...initial, feature({ name: 'Explorer Hub 2', categoryPath: 'Culture & Tourism > Visitor Center' })]

  assert.equal(searchPoiFeatures(initial, 'explorer').length, 1)
  assert.equal(searchPoiFeatures(refreshed, 'explorer').length, 2)
  assert.equal(searchPoiFeatures(refreshed, 'explorer', 1).length, 1)
})
