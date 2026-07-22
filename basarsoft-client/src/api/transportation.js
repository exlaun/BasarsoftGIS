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

export async function buildRoute(id) {
  const { data } = await client.post(`/api/routes/${id}/build`)
  return data
}

// Soft-deletes the route AND every stop on it (the server cascades). No body comes back.
export async function deleteRoute(id) {
  await client.delete(`/api/routes/${id}`)
}

// A route's stops in sequence order.
export async function listRouteStops(routeId) {
  const { data } = await client.get(`/api/routes/${routeId}/stops`)
  return data
}

// Persist a drag-reorder: the full set of the route's stop ids in the new order. Returns the
// authoritative stops plus the route rebuilt from that order.
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

// Returns { stops, route }: the route's surviving stops renumbered 1..N, plus the rebuilt route — the
// same shape reorderStops answers with, so callers reconcile both through one path.
export async function deleteStop(id) {
  const { data } = await client.delete(`/api/stops/${id}`)
  return data
}

// ---- Transportation administration (manage_transport_admin) ----
export async function getAdminTransportation() {
  const { data } = await client.get('/api/admin/transportation')
  return data
}

export async function updateAdminRoute(id, body) {
  const { data } = await client.put(`/api/admin/transportation/routes/${id}`, body)
  return data
}

export async function updateAdminStop(id, body) {
  const { data } = await client.put(`/api/admin/transportation/stops/${id}`, body)
  return data
}

export async function deleteAdminRoute(id) {
  await client.delete(`/api/admin/transportation/routes/${id}`)
}

export async function deleteAdminStop(id) {
  const { data } = await client.delete(`/api/admin/transportation/stops/${id}`)
  return data
}

export async function reorderAdminStops(routeId, orderedStopIds) {
  const { data } = await client.put(`/api/admin/transportation/routes/${routeId}/stops/order`, {
    orderedStopIds,
  })
  return data
}

export async function buildAdminRoute(routeId) {
  const { data } = await client.post(`/api/admin/transportation/routes/${routeId}/build`)
  return data
}
