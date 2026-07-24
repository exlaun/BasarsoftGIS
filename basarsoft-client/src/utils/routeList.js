const collator = new Intl.Collator('tr', { numeric: true, sensitivity: 'base' })

const foldText = (value) =>
  String(value ?? '')
    .toLocaleLowerCase('tr')
    .replace(/ı/g, 'i')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')

export function routeListState(route) {
  if (route?.isGeometryStale) return { key: 'stale', label: 'Needs rebuild' }
  if (route?.geometryWkt) return { key: 'ready', label: 'Built' }
  return { key: 'not-built', label: 'Not built' }
}

function comparable(route, sortBy) {
  if (sortBy === 'state') return routeListState(route).label
  if (sortBy === 'stopCount') return Number(route?.stopCount ?? 0)
  if (sortBy === 'createdAt') {
    const timestamp = Date.parse(route?.createdAt ?? '')
    return Number.isFinite(timestamp) ? timestamp : 0
  }
  return route?.name ?? ''
}

export function filterAndSortRoutes(
  routes,
  { query = '', sortBy = 'createdAt', sortDir = 'desc' } = {},
) {
  const foldedQuery = foldText(query.trim())
  const direction = sortDir === 'asc' ? 1 : -1

  return (routes ?? [])
    .map((route, index) => ({ route, index }))
    .filter(({ route }) => {
      return !foldedQuery || foldText(route.name).includes(foldedQuery)
    })
    .sort((left, right) => {
      const a = comparable(left.route, sortBy)
      const b = comparable(right.route, sortBy)
      const compared = typeof a === 'number' && typeof b === 'number'
        ? a - b
        : collator.compare(String(a), String(b))
      return compared === 0 ? left.index - right.index : compared * direction
    })
    .map(({ route }) => route)
}
