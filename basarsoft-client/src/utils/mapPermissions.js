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
