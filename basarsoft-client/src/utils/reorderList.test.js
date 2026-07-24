import test from 'node:test'
import assert from 'node:assert/strict'
import { moveItem } from './reorderList.js'

test('moves an item upward and preserves every item', () => {
  const items = ['A', 'B', 'C', 'D']
  assert.deepEqual(moveItem(items, 3, 1), ['A', 'D', 'B', 'C'])
  assert.deepEqual(items, ['A', 'B', 'C', 'D'])
})

test('moves an item downward', () => {
  const items = ['A', 'B', 'C', 'D']
  const reordered = moveItem(items, 0, 2)
  assert.deepEqual(reordered, ['B', 'C', 'A', 'D'])
  assert.deepEqual(reordered.sort(), items.slice().sort())
})

test('returns null for same-index and invalid moves', () => {
  const items = ['A', 'B', 'C']
  assert.equal(moveItem(items, 1, 1), null)
  assert.equal(moveItem(items, -1, 1), null)
  assert.equal(moveItem(items, 1, 3), null)
  assert.equal(moveItem(items, 1.5, 0), null)
})
