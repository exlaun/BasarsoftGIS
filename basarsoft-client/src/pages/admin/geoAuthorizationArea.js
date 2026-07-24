import Feature from 'ol/Feature.js'
import WKT from 'ol/format/WKT.js'
import MultiPolygon from 'ol/geom/MultiPolygon.js'
import Polygon from 'ol/geom/Polygon.js'

export const AUTH_AREA_DATA_PROJECTION = 'EPSG:4326'
export const AUTH_AREA_MAP_PROJECTION = 'EPSG:3857'

const wkt = new WKT()

function polygonCoordinates(geometry) {
  if (!geometry) return []

  if (geometry.getType() === 'Polygon') {
    return [geometry.getCoordinates()]
  }

  if (geometry.getType() === 'MultiPolygon') {
    return geometry.getCoordinates()
  }

  throw new TypeError('Authorization areas must be Polygon or MultiPolygon geometries.')
}

/**
 * Read an authorization WKT into one feature per polygon component.
 *
 * Splitting a MultiPolygon makes every disconnected component independently editable in the
 * OpenLayers Modify interaction. The components are combined again only when the form is saved.
 */
export function readAuthorizationAreaFeatures(
  value,
  {
    dataProjection = AUTH_AREA_DATA_PROJECTION,
    featureProjection = AUTH_AREA_MAP_PROJECTION,
  } = {},
) {
  const geometry = wkt.readGeometry(value, { dataProjection, featureProjection })

  return polygonCoordinates(geometry).map((coordinates) => new Feature(new Polygon(coordinates)))
}

/**
 * Combine every Polygon/MultiPolygon feature into one valid MultiPolygon.
 *
 * Returning MultiPolygon even for one component keeps the client contract aligned with the
 * normalized backend storage type and prevents disconnected regional areas from being collapsed.
 */
export function combineAuthorizationAreaFeatures(features) {
  const coordinates = features.flatMap((feature) => polygonCoordinates(feature?.getGeometry?.()))
  return coordinates.length > 0 ? new MultiPolygon(coordinates) : null
}

export function writeAuthorizationAreaWkt(
  features,
  {
    dataProjection = AUTH_AREA_DATA_PROJECTION,
    featureProjection = AUTH_AREA_MAP_PROJECTION,
  } = {},
) {
  const geometry = combineAuthorizationAreaFeatures(features)
  if (!geometry) return null

  return wkt.writeGeometry(geometry, { dataProjection, featureProjection })
}
