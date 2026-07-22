import { useLayoutEffect, useState } from 'react'
import { calculatePageCapacity } from './adaptivePagination'

// Measures the stable list viewport plus one representative collapsed row. Expanded route content
// deliberately does not influence the page size: it scrolls inside the same viewport instead.
export default function useAdaptivePageSize({
  containerRef,
  headerRef,
  rowRef,
  fallbackRowHeight,
  rowGap = 0,
  max = 100,
  measureKey,
}) {
  const [pageSize, setPageSize] = useState(10)

  useLayoutEffect(() => {
    const container = containerRef.current
    if (!container) return undefined

    const measure = () => {
      const styles = window.getComputedStyle(container)
      const verticalPadding = Number.parseFloat(styles.paddingTop || '0')
        + Number.parseFloat(styles.paddingBottom || '0')
      const containerHeight = Math.max(0, container.clientHeight - verticalPadding)
      const reservedHeight = headerRef?.current?.getBoundingClientRect().height ?? 0
      const rowHeight = rowRef.current?.getBoundingClientRect().height || fallbackRowHeight
      const next = calculatePageCapacity({
        containerHeight,
        reservedHeight,
        rowHeight,
        rowGap,
        max,
      })
      setPageSize((current) => (current === next ? current : next))
    }

    measure()
    const observer = new ResizeObserver(measure)
    observer.observe(container)
    if (headerRef?.current) observer.observe(headerRef.current)
    if (rowRef.current) observer.observe(rowRef.current)
    window.addEventListener('resize', measure)
    return () => {
      observer.disconnect()
      window.removeEventListener('resize', measure)
    }
  }, [containerRef, headerRef, rowRef, fallbackRowHeight, rowGap, max, measureKey])

  return pageSize
}
