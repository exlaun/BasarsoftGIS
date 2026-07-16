const foldPoiSearchText = (value) =>
  value
    .toLocaleLowerCase('tr')
    .replace(/ı/g, 'i')
    .normalize('NFD')
    .replace(/[\u0300-\u036f]/g, '')

const matches = (value, query) =>
  Boolean(value) && foldPoiSearchText(value).includes(foldPoiSearchText(query))

// Name matches rank above category-only matches. Stop after enough name hits because no later
// category hit can outrank them; otherwise keep category matches in source order.
export function searchPoiFeatures(features, query, maxResults = 8) {
  const trimmed = query.trim()
  if (!trimmed || maxResults <= 0) return []

  const nameHits = []
  const pathHits = []
  for (const feature of features ?? []) {
    if (matches(feature.get('name'), trimmed)) nameHits.push(feature)
    else if (matches(feature.get('categoryPath'), trimmed)) pathHits.push(feature)
    if (nameHits.length >= maxResults) break
  }

  return [...nameHits, ...pathHits].slice(0, maxResults)
}

export { foldPoiSearchText }
