export function disabledMapTools(toolConfig, permissions) {
  const granted = new Set(permissions)
  return new Set(
    Object.entries(toolConfig)
      .filter(([, config]) => !granted.has(config.permission))
      .map(([tool]) => tool),
  )
}

export function canDeletePoi(permissions) {
  return permissions.includes('manage_pois')
}

// Every vertex of `geometry` must fall inside `authGeom`. A vertex test can miss an edge that dips
// outside between two inside vertices, so this is fast feedback only — the API re-checks every write
// with full geometry containment and stays the authority. A null boundary means unrestricted, which
// mirrors the server, where a user with no assigned area is unrestricted.
export function isInsideAuthorizedArea(geometry, authGeom) {
  if (!authGeom || !geometry) return true
  const type = geometry.getType()
  const vertices =
    type === 'Point'
      ? [geometry.getCoordinates()]
      : type === 'LineString'
        ? geometry.getCoordinates()
        : geometry.getCoordinates()[0] // Polygon outer ring
  return vertices.every((coord) => authGeom.intersectsCoordinate(coord))
}
