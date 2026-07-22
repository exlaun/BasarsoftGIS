import assert from 'node:assert/strict'
import test from 'node:test'
import {
  calculatePageCapacity,
  clampPage,
  pageForItem,
  pageSlice,
  rebasePage,
} from './adaptivePagination.js'

test('adaptive capacity fills available height and respects bounds', () => {
  assert.equal(calculatePageCapacity({ containerHeight: 500, reservedHeight: 32, rowHeight: 36 }), 13)
  assert.equal(calculatePageCapacity({ containerHeight: 10, rowHeight: 36 }), 1)
  assert.equal(calculatePageCapacity({ containerHeight: 10000, rowHeight: 36, max: 100 }), 100)
})

test('page rebasing preserves the first visible item', () => {
  assert.equal(rebasePage(4, 10, 25), 2)
  assert.equal(rebasePage(2, 25, 10), 3)
})

test('route paging slices and clamps consistently', () => {
  const routes = Array.from({ length: 12 }, (_, index) => index + 1)
  assert.deepEqual(pageSlice(routes, 2, 5), [6, 7, 8, 9, 10])
  assert.equal(pageForItem(10, 5), 3)
  assert.equal(clampPage(4, routes.length, 5), 3)
})
