import test from 'node:test'
import assert from 'node:assert/strict'
import { compareAdminValues, sortAdminRows } from './adminTableModel.js'

const columns = [
  { key: 'name', sortValue: (row) => row.name },
  { key: 'count', sortType: 'number', sortValue: (row) => row.count },
  { key: 'createdAt', sortType: 'date', sortValue: (row) => row.createdAt },
]

const rows = [
  { id: 1, name: 'İzmir 2', count: 10, createdAt: '2026-07-02' },
  { id: 2, name: 'Ankara', count: null, createdAt: '2026-07-01' },
  { id: 3, name: 'İzmir 10', count: 2, createdAt: '2026-07-03' },
  { id: 4, name: '', count: 2, createdAt: '' },
]

test('admin values compare text, numbers, dates, and empty values', () => {
  assert.equal(compareAdminValues('İzmir 2', 'İzmir 10'), -1)
  assert.equal(compareAdminValues(2, 10, 'number'), -1)
  assert.equal(compareAdminValues('2026-07-03', '2026-07-01', 'date'), 1)
  assert.equal(compareAdminValues('', 'Ankara'), 1)
})

test('admin rows sort stably in ascending and descending directions', () => {
  assert.deepEqual(sortAdminRows(rows, columns, 'name', 'asc').map((row) => row.id), [2, 1, 3, 4])
  assert.deepEqual(sortAdminRows(rows, columns, 'count', 'desc').map((row) => row.id), [1, 3, 4, 2])
  assert.deepEqual(sortAdminRows(rows, columns, 'createdAt', 'asc').map((row) => row.id), [2, 1, 3, 4])
})

test('admin columns are sortable by default and can opt out explicitly', () => {
  assert.deepEqual(sortAdminRows(rows, columns, 'name', 'desc').map((row) => row.id), [3, 1, 2, 4])
  assert.deepEqual(sortAdminRows(rows, [{ key: 'name', sortable: false, sortValue: (row) => row.name }], 'name'), rows)
})
