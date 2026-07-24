import client from './client'

// Shared province-reference overlay: each item contains a province boundary and its administrative
// capital point in EPSG:4326 WKT, plus the one color used to visually link the pair.
export async function listProvinceMap() {
  const { data } = await client.get('/api/provinces/map')
  return data
}
