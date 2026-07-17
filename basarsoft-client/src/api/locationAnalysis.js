import client from './client'

// Konum Analizi (location analysis) endpoints. All open to any authenticated user — the tool has no
// permission gate (the mentor's requirement: the plain User role must be able to run analyses).

// The 81 provinces for the region dropdown: [{ id, name }], sorted by name server-side.
export async function listProvinces() {
  const { data } = await client.get('/api/provinces')
  return data
}

// One province's boundary as WKT (EPSG:4326 MultiPolygon) — fetched on selection so the map can
// draw the region outline and zoom to it.
export async function getProvince(id) {
  const { data } = await client.get(`/api/provinces/${id}`)
  return data
}

// Validate + store an analysis run. Exactly one of provinceId/regionWkt; criteria = 2..5 rows of
// { categoryId, weight } whose weights must sum to exactly 100 (the server re-checks everything and
// answers 400 with a `code` per broken rule). Returns { id, regionWkt, matchedPoiCount, criteria... };
// the id then drives GET /api/location-analysis/{id}/wms (the weighted heat map layer).
export async function createLocationAnalysis({ provinceId, regionWkt, criteria }) {
  const { data } = await client.post('/api/location-analysis', {
    provinceId: provinceId ?? null,
    regionWkt: regionWkt ?? null,
    criteria,
  })
  return data
}
