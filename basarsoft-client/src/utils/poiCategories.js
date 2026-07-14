// Helpers shared by every POI category UI (admin tree, parent picker, operator dropdown).
// The API returns categories FLAT ({ id, name, parentId, poiCount }); these rebuild the hierarchy.

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

// "09:30:00" (API) -> "09:30" (display / <input type="time"> value).
export function formatTime(time) {
  return typeof time === 'string' ? time.slice(0, 5) : ''
}
