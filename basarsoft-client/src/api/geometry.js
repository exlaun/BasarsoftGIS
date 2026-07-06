import client from './client'

// Thin wrappers over the /api/geometry endpoints. `type` is 'point' | 'line' | 'polygon'.
// The bearer token is attached automatically by the axios client interceptor.

// Every shape the logged-in user owns, grouped { points, lines, polygons }, for a one-shot map load.
export async function listAllGeometry() {
  const { data } = await client.get('/api/geometry')
  return data
}

// Save a drawn shape. `wkt` is Well-Known Text in EPSG:4326 (lon/lat). `name` and `color` come from
// the attribute popup. Polygon creates also return `intersectionCount`: existing shapes fully within
// the saved polygon (stricter than the temporary Analysis tool's intersects count).
export async function saveGeometry(type, wkt, name, color) {
  const { data } = await client.post(`/api/geometry/${type}`, {
    wkt,
    name, // required — the attribute popup blocks save until a non-empty name is entered
    color: color || null,
  })
  return data
}

// Update one of the user's own shapes. `name`/`color` are always sent; `wkt` (EPSG:4326) is optional —
// omit it for an attribute-only edit, include it to move/reshape the geometry. Returns the updated row.
export async function updateGeometry(type, id, { wkt, name, color }) {
  const { data } = await client.put(`/api/geometry/${type}/${id}`, {
    name,
    color: color || null,
    wkt: wkt || null,
  })
  return data
}

// Soft-delete one of the user's own shapes.
export async function deleteGeometry(type, id) {
  await client.delete(`/api/geometry/${type}/${id}`)
}

// Inventory analysis: send a temporary polygon (WKT, EPSG:4326) and get back how many of the user's
// shapes intersect it, broken down by type. The polygon is NOT saved on the server.
export async function analyzeArea(wkt) {
  const { data } = await client.post('/api/geometry/analysis', { wkt })
  return data
}

// Query panel: one page of the user's shapes as flat rows { items, total, page, pageSize }.
// Filtering (name/types), sorting and paging all happen in SQL on the server — params are
// { name?, types? (CSV), sortBy, sortDir, page, pageSize }.
export async function queryGeometry(params) {
  const { data } = await client.get('/api/geometry/query', { params })
  return data
}
