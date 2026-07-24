// Return a new list with one item moved to another index. Invalid or no-op moves return null so
// drag-and-drop callers can skip an unnecessary API request.
export function moveItem(items, fromIndex, toIndex) {
  if (!Array.isArray(items) || !Number.isInteger(fromIndex) || !Number.isInteger(toIndex)) return null
  if (fromIndex < 0 || fromIndex >= items.length || toIndex < 0 || toIndex >= items.length) return null
  if (fromIndex === toIndex) return null

  const next = [...items]
  const [moved] = next.splice(fromIndex, 1)
  next.splice(toIndex, 0, moved)
  return next
}

