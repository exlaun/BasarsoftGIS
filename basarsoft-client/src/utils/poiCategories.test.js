import assert from 'node:assert/strict'
import { readFile } from 'node:fs/promises'
import test from 'node:test'
import {
  DEFAULT_POI_COLOR,
  POI_ICON_CATALOG,
  categoryBadgeAppearance,
  effectiveCategoryIconKey,
  normalizePoiIconKey,
  poiIconUrl,
} from './poiCategories.js'

test('normalizes icon keys to the fixed allowlist and safe pin fallback', () => {
  assert.equal(normalizePoiIconKey(' Coffee '), 'coffee')
  assert.equal(normalizePoiIconKey('../mail'), 'pin')
  assert.equal(normalizePoiIconKey(null), 'pin')
  assert.equal(poiIconUrl('airport'), '/poi-icons/airport.svg')
  assert.equal(poiIconUrl('COFFEE', '/turkey-explorer/'), '/turkey-explorer/poi-icons/coffee.svg')
  assert.equal(poiIconUrl('unknown', '/demo'), '/demo/poi-icons/pin.svg')
})

test('selects the nearest inherited category icon and effective badge appearance', () => {
  const categories = [
    { id: 1, parentId: null, color: '#1d4ed8', iconKey: 'culture' },
    { id: 2, parentId: 1, color: null, iconKey: null },
    { id: 3, parentId: 2, color: '#7c3aed', iconKey: 'museum' },
    { id: 4, parentId: null, color: null, iconKey: null },
  ]

  assert.equal(effectiveCategoryIconKey(categories, 2), 'culture')
  assert.deepEqual(categoryBadgeAppearance(categories, 2), {
    color: '#1d4ed8',
    iconKey: 'culture',
  })
  assert.deepEqual(categoryBadgeAppearance(categories, 3), {
    color: '#7c3aed',
    iconKey: 'museum',
  })
  assert.deepEqual(categoryBadgeAppearance(categories, 4), {
    color: DEFAULT_POI_COLOR,
    iconKey: 'pin',
  })
})

test('every allowlisted icon has a fixed-white canonical SVG asset', async () => {
  assert.equal(POI_ICON_CATALOG.length, 20)
  assert.equal(new Set(POI_ICON_CATALOG.map(({ key }) => key)).size, 20)

  await Promise.all(
    POI_ICON_CATALOG.map(async ({ key }) => {
      const source = await readFile(
        new URL(`../../public/poi-icons/${key}.svg`, import.meta.url),
        'utf8',
      )
      assert.match(source, /viewBox="0 0 24 24"/)
      assert.match(source, /stroke="#fff"/)
      assert.doesNotMatch(source, /currentColor/)
    }),
  )

  const license = await readFile(
    new URL('../../public/poi-icons/LICENSE.md', import.meta.url),
    'utf8',
  )
  assert.match(license, /ISC License/)
  assert.match(license, /Lucide Contributors/)
})
