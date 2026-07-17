// Helpers shared by every POI category UI (admin tree, parent picker, operator dropdown).
// The API returns categories FLAT ({ id, name, parentId, poiCount }); these rebuild the hierarchy.

export const DEFAULT_POI_COLOR = '#e11d48'
export const DEFAULT_POI_ICON_KEY = 'pin'

// Kept in the same order as GET /api/poi/icons so the fallback UI and the API catalogue are stable.
// The endpoint remains authoritative for admin-facing labels; this local allowlist protects asset
// URLs and lets map rendering normalize old/corrupt feature data without constructing arbitrary paths.
export const POI_ICON_CATALOG = Object.freeze([
  { key: 'pin', label: 'Pin' },
  { key: 'food', label: 'Food & Drink' },
  { key: 'coffee', label: 'Coffee' },
  { key: 'bakery', label: 'Bakery' },
  { key: 'health', label: 'Health' },
  { key: 'pharmacy', label: 'Pharmacy' },
  { key: 'shopping', label: 'Shopping' },
  { key: 'culture', label: 'Culture & Tourism' },
  { key: 'museum', label: 'Museum' },
  { key: 'hotel', label: 'Hotel' },
  { key: 'services', label: 'Services' },
  { key: 'bank', label: 'Bank' },
  { key: 'fuel', label: 'Fuel' },
  { key: 'transport', label: 'Transport' },
  { key: 'airport', label: 'Airport' },
  { key: 'education', label: 'Education' },
  { key: 'nature', label: 'Nature & Recreation' },
  { key: 'sports', label: 'Sports' },
  { key: 'mail', label: 'Post Office' },
  { key: 'government', label: 'Government' },
])

const POI_ICON_KEYS = new Set(POI_ICON_CATALOG.map(({ key }) => key))

export function normalizePoiIconKey(value) {
  const key = typeof value === 'string' ? value.trim().toLowerCase() : ''
  return POI_ICON_KEYS.has(key) ? key : DEFAULT_POI_ICON_KEY
}

// Public assets are deliberately addressed from a caller-supplied base so the helper remains usable
// in Vite deployments mounted below "/" and deterministic in the dependency-free Node tests.
export function poiIconUrl(iconKey, baseUrl = '/') {
  const base = baseUrl.endsWith('/') ? baseUrl : `${baseUrl}/`
  return `${base}poi-icons/${normalizePoiIconKey(iconKey)}.svg`
}

// Depth-first flatten: roots first (alphabetical), each followed by its children, with a `depth`
// added for indentation. Orphans (parent missing/soft-deleted mid-session) surface as roots rather
// than vanishing. A visited-set guards against a corrupt parent cycle looping forever.
export function flattenCategoryTree(categories) {
  const byParent = new Map()
  const ids = new Set(categories.map((c) => c.id))
  for (const category of categories) {
    const key = category.parentId != null && ids.has(category.parentId) ? category.parentId : null
    if (!byParent.has(key)) byParent.set(key, [])
    byParent.get(key).push(category)
  }
  for (const group of byParent.values()) group.sort((a, b) => a.name.localeCompare(b.name, 'tr'))

  const result = []
  const visited = new Set()
  const walk = (parentKey, depth) => {
    for (const category of byParent.get(parentKey) ?? []) {
      if (visited.has(category.id)) continue
      visited.add(category.id)
      result.push({ ...category, depth })
      walk(category.id, depth + 1)
    }
  }
  walk(null, 0)
  return result
}

// Ids of a category and everything below it — the set a category may NOT pick as its new parent
// (re-parenting into your own subtree would create a cycle; the server double-checks).
export function collectDescendantIds(categories, rootId) {
  const excluded = new Set([rootId])
  let grew = true
  while (grew) {
    grew = false
    for (const category of categories) {
      if (category.parentId != null && excluded.has(category.parentId) && !excluded.has(category.id)) {
        excluded.add(category.id)
        grew = true
      }
    }
  }
  return excluded
}

// "— — Cafe" style label for flat <option> lists (native selects can't nest beyond one level).
export function categoryOptionLabel(category) {
  return `${'— '.repeat(category.depth)}${category.name}`
}

// Root-first breadcrumb ("Yeme İçme > Restoran") from the flat list, for places that only have a
// category id + the list (the API also precomputes this as poi.categoryPath).
export function categoryPathOf(categories, id) {
  const byId = new Map(categories.map((c) => [c.id, c]))
  const parts = []
  let current = byId.get(id)
  for (let depth = 0; current && depth < 20; depth++) {
    parts.unshift(current.name)
    current = current.parentId != null ? byId.get(current.parentId) : null
  }
  return parts.join(' > ')
}

// Effective display color for a category: its own color, else the nearest colored ancestor's, else
// null (caller falls back to the default POI rose). Client-side mirror of the inheritance walk the
// server does in PoiService.EffectiveColor / the vw_poi SQL view, same 20-step cycle cap.
export function effectiveCategoryColor(categories, id) {
  const byId = new Map(categories.map((c) => [c.id, c]))
  let current = byId.get(id)
  for (let depth = 0; current && depth < 20; depth++) {
    if (current.color) return current.color
    current = current.parentId != null ? byId.get(current.parentId) : null
  }
  return null
}

// Effective icon mirrors effectiveCategoryColor: null means inherit, while a missing ancestor chain
// falls back to the canonical pin. Non-null unknown values are normalized to pin defensively; the
// API rejects them on writes, but this keeps a stale WFS response from becoming an asset-path input.
export function effectiveCategoryIconKey(categories, id) {
  const byId = new Map(categories.map((c) => [c.id, c]))
  let current = byId.get(id)
  for (let depth = 0; current && depth < 20; depth++) {
    if (current.iconKey != null && String(current.iconKey).trim() !== '') {
      return normalizePoiIconKey(current.iconKey)
    }
    current = current.parentId != null ? byId.get(current.parentId) : null
  }
  return DEFAULT_POI_ICON_KEY
}

// One pure descriptor used by every React badge site. Keeping this decision outside the component
// makes category-tree inheritance and fallback behavior straightforward to contract-test.
export function categoryBadgeAppearance(categories, id, fallbackColor = DEFAULT_POI_COLOR) {
  return {
    color: effectiveCategoryColor(categories, id) || fallbackColor,
    iconKey: effectiveCategoryIconKey(categories, id),
  }
}

// "09:30:00" (API) -> "09:30" (display / <input type="time"> value).
export function formatTime(time) {
  return typeof time === 'string' ? time.slice(0, 5) : ''
}
