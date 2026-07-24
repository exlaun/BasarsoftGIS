import WKT from 'ol/format/WKT.js'

const format = new WKT()
const DATA_PROJECTION = 'EPSG:4326'
const MAP_PROJECTION = 'EPSG:3857'

export const DEFAULT_PROVINCE_COLOR = '#64748b'

function normalizedColor(value) {
  return typeof value === 'string' && /^#[0-9a-f]{6}$/i.test(value)
    ? value
    : DEFAULT_PROVINCE_COLOR
}

function stampProvinceFeature(feature, item, kind, color) {
  feature.set('provinceId', item.id)
  feature.set('provinceFeatureKind', kind)
  feature.set('provinceColor', color)
  feature.set('provinceName', item.name)
  feature.set('capitalName', item.capitalName)
  feature.set(
    'name',
    kind === 'capital'
      ? `${item.capitalName || item.name} · ${item.name}`
      : item.name,
  )
}

// Invalid or incomplete pairs are ignored together: rendering a boundary without its capital (or
// vice versa) would break the visual relationship this reference layer is designed to communicate.
export function parseProvinceMapItem(item) {
  if (
    item?.id == null
    || typeof item.boundaryWkt !== 'string'
    || typeof item.capitalWkt !== 'string'
  ) {
    return null
  }

  try {
    const boundary = format.readFeature(item.boundaryWkt, {
      dataProjection: DATA_PROJECTION,
      featureProjection: MAP_PROJECTION,
    })
    const capital = format.readFeature(item.capitalWkt, {
      dataProjection: DATA_PROJECTION,
      featureProjection: MAP_PROJECTION,
    })
    const boundaryType = boundary.getGeometry()?.getType()
    if (!['Polygon', 'MultiPolygon'].includes(boundaryType)) return null
    if (capital.getGeometry()?.getType() !== 'Point') return null

    const color = normalizedColor(item.color)
    stampProvinceFeature(boundary, item, 'boundary', color)
    stampProvinceFeature(capital, item, 'capital', color)
    return { boundary, capital }
  } catch {
    return null
  }
}

export function parseProvinceMapItems(items) {
  const pairs = (items ?? []).map(parseProvinceMapItem).filter(Boolean)
  return {
    boundaries: pairs.map(({ boundary }) => boundary),
    capitals: pairs.map(({ capital }) => capital),
  }
}

export function isProvinceFeatureHighlighted(feature, hoveredProvinceId, selectedProvinceId) {
  const provinceId = feature?.get('provinceId')
  return provinceId != null
    && (provinceId === hoveredProvinceId || provinceId === selectedProvinceId)
}

// OpenLayers returns hit features from top to bottom. A capital and its boundary deliberately carry
// the same id, so either one resolves to the same linked selection; a click with no province hit
// resolves to null and clears the previous highlight.
export function selectedProvinceIdFromFeatures(features) {
  for (const feature of features ?? []) {
    const provinceId = feature?.get?.('provinceId')
    if (provinceId != null) return provinceId
  }
  return null
}
