import client from './client'

// Thin wrappers over the POI endpoints. Reads (/api/poi, /api/poi/categories) are open to every
// authenticated user — the POI catalogue is shared. Creating needs the add_poi permission and
// category writes need admin access; the server enforces both.

// ---- POIs ----
export async function listPois() {
  const { data } = await client.get('/api/poi')
  return data
}

export async function createPoi(wkt, name, categoryId, openTime, closeTime) {
  // openTime/closeTime are "HH:mm" straight from <input type="time">; the API accepts that.
  const { data } = await client.post('/api/poi', { wkt, name, categoryId, openTime, closeTime })
  return data
}

export async function deletePoi(id) {
  await client.delete(`/api/poi/${id}`)
}

// ---- Categories ----
// Flat list { id, name, parentId, color, iconKey, poiCount }; the tree is rebuilt client-side from
// parentId. `color` and `iconKey` are the category's OWN values or null (= inherit from an ancestor).
export async function listPoiCategories() {
  const { data } = await client.get('/api/poi/categories')
  return data
}

// Authoritative marker-icon allowlist and admin-facing labels: [{ key, label }].
export async function listPoiIcons() {
  const { data } = await client.get('/api/poi/icons')
  return data
}

export async function createPoiCategory(body) {
  // body = { name, parentId?, color?, iconKey? }
  const { data } = await client.post('/api/admin/poi-categories', body)
  return data
}

export async function updatePoiCategory(id, body) {
  const { data } = await client.put(`/api/admin/poi-categories/${id}`, body)
  return data
}

export async function deletePoiCategory(id) {
  await client.delete(`/api/admin/poi-categories/${id}`)
}
