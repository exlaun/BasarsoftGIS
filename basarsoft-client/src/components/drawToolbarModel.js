export function isDisplayOnlyMapMode(displayMode) {
  return displayMode === 'wms'
}

export function visibleToolbarGroups(groups, disabledTools = new Set(), displayOnly = false) {
  if (displayOnly) return groups.filter((entry) => entry.kind === 'layers')

  return groups
    .map((entry) =>
      entry.kind === 'group'
        ? { ...entry, children: entry.children.filter((child) => !disabledTools.has(child.key)) }
        : entry,
    )
    .filter((entry) => entry.kind !== 'group' || entry.children.length > 0)
}

export function visibleLayerEntries(layers, displayOnly = false) {
  return displayOnly ? layers.filter((entry) => entry.type === 'province') : layers
}

// The toolbar lives inside `.map-body`, whose overflow is deliberately clipped. Return an offset
// relative to the Layers button's wrapper that keeps the flyout inside that clipped rectangle,
// including when the one-button WMS rail is vertically centred or the legend is expanded.
export function calculateLayerFlyoutPlacement({
  containerTop,
  containerBottom,
  anchorTop,
  anchorBottom,
  contentHeight,
  padding = 12,
}) {
  const usableTop = containerTop + padding
  const usableBottom = Math.max(usableTop, containerBottom - padding)
  const maxHeight = Math.max(0, usableBottom - usableTop)
  const renderedHeight = Math.min(Math.max(0, contentHeight), maxHeight)
  const anchorCenter = (anchorTop + anchorBottom) / 2
  const centeredTop = anchorCenter - renderedHeight / 2
  const absoluteTop = Math.min(
    Math.max(centeredTop, usableTop),
    Math.max(usableTop, usableBottom - renderedHeight),
  )

  return {
    top: absoluteTop - anchorTop,
    maxHeight,
  }
}
