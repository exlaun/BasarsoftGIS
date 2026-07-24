const collator = new Intl.Collator('tr', { numeric: true, sensitivity: 'base' })

const isEmpty = (value) => value == null || String(value).trim() === ''

function comparableValue(value, type) {
  if (type === 'number') {
    const number = Number(value)
    return Number.isFinite(number) ? number : null
  }

  if (type === 'date') {
    const timestamp = value instanceof Date ? value.getTime() : Date.parse(value ?? '')
    return Number.isFinite(timestamp) ? timestamp : null
  }

  return String(value)
}

export function compareAdminValues(left, right, type = 'text') {
  const leftEmpty = isEmpty(left)
  const rightEmpty = isEmpty(right)
  if (leftEmpty || rightEmpty) {
    if (leftEmpty && rightEmpty) return 0
    return leftEmpty ? 1 : -1
  }

  const a = comparableValue(left, type)
  const b = comparableValue(right, type)
  if (a == null || b == null) {
    if (a == null && b == null) return 0
    return a == null ? 1 : -1
  }
  if (type === 'text') return collator.compare(a, b)
  return a === b ? 0 : a < b ? -1 : 1
}

export function sortAdminRows(rows, columns, sortKey, sortDir = 'asc') {
  if (!sortKey) return rows
  const column = columns.find((item) => item.key === sortKey)
  if (!column || column.sortable === false) return rows

  const direction = sortDir === 'desc' ? -1 : 1
  return (rows ?? [])
    .map((row, index) => ({ row, index }))
    .sort((left, right) => {
      const leftValue = column.sortValue ? column.sortValue(left.row) : left.row[column.key]
      const rightValue = column.sortValue ? column.sortValue(right.row) : right.row[column.key]
      const leftEmpty = isEmpty(leftValue)
      const rightEmpty = isEmpty(rightValue)
      if (leftEmpty || rightEmpty) {
        if (leftEmpty && rightEmpty) return left.index - right.index
        return leftEmpty ? 1 : -1
      }
      const compared = compareAdminValues(leftValue, rightValue, column.sortType)
      return compared === 0 ? left.index - right.index : compared * direction
    })
    .map(({ row }) => row)
}
