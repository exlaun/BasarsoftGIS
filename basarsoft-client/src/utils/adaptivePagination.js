export const clampPage = (page, totalItems, pageSize) => {
  const totalPages = Math.max(1, Math.ceil(totalItems / Math.max(1, pageSize)))
  return Math.min(Math.max(1, page), totalPages)
}

export const rebasePage = (page, previousPageSize, nextPageSize) => {
  const firstVisibleIndex = (Math.max(1, page) - 1) * Math.max(1, previousPageSize)
  return Math.floor(firstVisibleIndex / Math.max(1, nextPageSize)) + 1
}

export const pageForItem = (itemIndex, pageSize) =>
  Math.floor(Math.max(0, itemIndex) / Math.max(1, pageSize)) + 1

export const pageSlice = (items, page, pageSize) => {
  const start = (Math.max(1, page) - 1) * Math.max(1, pageSize)
  return items.slice(start, start + Math.max(1, pageSize))
}

export const calculatePageCapacity = ({
  containerHeight,
  reservedHeight = 0,
  rowHeight,
  rowGap = 0,
  min = 1,
  max = 100,
}) => {
  const footprint = Math.max(1, rowHeight + rowGap)
  const available = Math.max(0, containerHeight - reservedHeight)
  return Math.min(max, Math.max(min, Math.floor((available + rowGap) / footprint)))
}
