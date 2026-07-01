import client from './client'

// Thin wrappers over the /api/geometry endpoints. `type` is 'point' | 'line' | 'polygon'.
// The bearer token is attached automatically by the axios client interceptor.

// Every shape the logged-in user owns, grouped { points, lines, polygons }, for a one-shot map load.
export async function listAllGeometry() {
  const { data } = await client.get('/api/geometry')
  return data
}

// Save a drawn shape. `wkt` is Well-Known Text in EPSG:4326 (lon/lat). Returns the saved row.
export async function saveGeometry(type, wkt, name) {
  const { data } = await client.post(`/api/geometry/${type}`, { wkt, name: name || null })
  return data
}

// Soft-delete one of the user's own shapes.
export async function deleteGeometry(type, id) {
  await client.delete(`/api/geometry/${type}/${id}`)
}
