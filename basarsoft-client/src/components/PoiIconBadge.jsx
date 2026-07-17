import { DEFAULT_POI_COLOR, normalizePoiIconKey, poiIconUrl } from '../utils/poiCategories'
import './PoiIconBadge.css'

// Reusable POI identity: category color carries broad grouping while the white glyph distinguishes
// categories that share or inherit that color. The same public SVG is used by OpenLayers/GeoServer.
export default function PoiIconBadge({
  iconKey,
  color = DEFAULT_POI_COLOR,
  size = 20,
  label,
  className = '',
}) {
  const normalized = normalizePoiIconKey(iconKey)
  const classes = `poi-icon-badge${className ? ` ${className}` : ''}`

  return (
    <span
      className={classes}
      style={{ '--poi-badge-color': color || DEFAULT_POI_COLOR, '--poi-badge-size': `${size}px` }}
      role={label ? 'img' : undefined}
      aria-label={label}
      aria-hidden={label ? undefined : 'true'}
      title={label}
    >
      <img src={poiIconUrl(normalized, import.meta.env.BASE_URL)} alt="" draggable="false" />
    </span>
  )
}
