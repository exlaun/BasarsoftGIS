import test from 'node:test'
import assert from 'node:assert/strict'
import { filterAndSortRoutes, routeListState } from './routeList.js'

const routes = [
  { id: 1, name: 'İzmir Ring', stopCount: 3, geometryWkt: 'LINESTRING(0 0,1 1)', createdAt: '2026-06-01' },
  { id: 2, name: 'Ankara Loop', stopCount: 8, isGeometryStale: true, createdAt: '2026-06-03' },
  { id: 3, name: 'Bursa Shuttle', stopCount: 1, geometryWkt: null, createdAt: '2026-06-02' },
]

test('route list derives health states and folds Turkish text searches', () => {
  assert.equal(routeListState(routes[0]).key, 'ready')
  assert.equal(routeListState(routes[0]).label, 'Built')
  assert.equal(routeListState(routes[1]).key, 'stale')
  assert.equal(routeListState(routes[1]).label, 'Needs rebuild')
  assert.equal(routeListState(routes[2]).key, 'not-built')
  assert.equal(routeListState(routes[2]).label, 'Not built')
  assert.deepEqual(filterAndSortRoutes(routes, { query: 'izmir' }).map((route) => route.id), [1])
})

test('route list sorts every visible table attribute', () => {
  assert.deepEqual(filterAndSortRoutes(routes, {
    sortBy: 'stopCount',
    sortDir: 'desc',
  }).map((route) => route.id), [2, 1, 3])
  assert.deepEqual(filterAndSortRoutes(routes, {
    sortBy: 'createdAt',
    sortDir: 'asc',
  }).map((route) => route.id), [1, 3, 2])
  assert.deepEqual(filterAndSortRoutes(routes, {
    sortBy: 'name',
    sortDir: 'asc',
  }).map((route) => route.id), [2, 3, 1])
})
