import client from './client'

// Thin wrappers over the transportation endpoints. Reads (/api/routes, /api/stops) are open to every
// authenticated user (End Users browse read-only); writes require the manage_transport permission,
// which the server enforces. Mirrors the shape of api/poi.js over the shared axios client.

// ---- Routes ----
export async function listRoutes() {
  const { data } = await client.get('/api/routes')
  return data
}

export async function createRoute(body) {
  // body = { name, color? }
  const { data } = await client.post('/api/routes', body)
  return data
}

export async function updateRoute(id, body) {
  const { data } = await client.put(`/api/routes/${id}`, body)
  return data
}

// A route's stops in sequence order.
export async function listRouteStops(routeId) {
  const { data } = await client.get(`/api/routes/${routeId}/stops`)
  return data
}

// Persist a drag-reorder: the full set of the route's stop ids in the new order. Returns the
// reordered stops (with their new sequenceOrder) so the caller can resync.
export async function reorderStops(routeId, orderedStopIds) {
  const { data } = await client.put(`/api/routes/${routeId}/stops/order`, { orderedStopIds })
  return data
}

// ---- Stops ----
// Every stop in the system (route + sequence order), for the map layer.
export async function listStops() {
  const { data } = await client.get('/api/stops')
  return data
}

export async function createStop(wkt, name, routeId) {
  const { data } = await client.post('/api/stops', { wkt, name, routeId })
  return data
}
