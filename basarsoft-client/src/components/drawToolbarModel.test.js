import assert from 'node:assert/strict'
import test from 'node:test'
import {
  calculateLayerFlyoutPlacement,
  isDisplayOnlyMapMode,
  visibleLayerEntries,
  visibleToolbarGroups,
} from './drawToolbarModel.js'

const toolbarGroups = [
  { kind: 'tool', key: 'none', label: 'Pan' },
  { kind: 'tool', key: 'select', label: 'Select' },
  {
    kind: 'group',
    id: 'draw',
    children: [
      { key: 'Point' },
      { key: 'Polygon' },
    ],
  },
  { kind: 'layers', id: 'layers', label: 'Layers' },
]

test('WMS display mode keeps Layers while withholding editing controls', () => {
  assert.equal(isDisplayOnlyMapMode('wms'), true)
  assert.equal(isDisplayOnlyMapMode('vector'), false)

  const groups = visibleToolbarGroups(toolbarGroups, new Set(), true)
  assert.deepEqual(groups.map((entry) => entry.id ?? entry.key), ['layers'])

  const layers = visibleLayerEntries(
    [
      { type: 'point' },
      { type: 'line' },
      { type: 'polygon' },
      { type: 'province' },
    ],
    true,
  )
  assert.deepEqual(layers.map(({ type }) => type), ['province'])
})

test('WFS toolbar still applies permission filtering without removing Layers', () => {
  const groups = visibleToolbarGroups(toolbarGroups, new Set(['Point']), false)
  assert.deepEqual(groups.map((entry) => entry.id ?? entry.key), [
    'none',
    'select',
    'draw',
    'layers',
  ])
  assert.deepEqual(groups[2].children.map(({ key }) => key), ['Polygon'])
})

test('Layers flyout is clamped to the clipped map body above and below', () => {
  const nearBottom = calculateLayerFlyoutPlacement({
    containerTop: 100,
    containerBottom: 700,
    anchorTop: 640,
    anchorBottom: 680,
    contentHeight: 320,
  })
  assert.equal(640 + nearBottom.top + 320, 688)
  assert.equal(nearBottom.maxHeight, 576)

  const tooTall = calculateLayerFlyoutPlacement({
    containerTop: 100,
    containerBottom: 300,
    anchorTop: 180,
    anchorBottom: 220,
    contentHeight: 500,
  })
  assert.equal(tooTall.top, -68)
  assert.equal(tooTall.maxHeight, 176)
  assert.equal(180 + tooTall.top, 112)
})
