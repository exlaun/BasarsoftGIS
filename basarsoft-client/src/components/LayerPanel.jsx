import './LayerPanel.css'
import { DEMO_DRAWING_THEMES, DEMO_THEME_NOTE } from '../utils/demoThemes'
import { visibleLayerEntries } from './drawToolbarModel'

// Inline feather-style icons (inherit currentColor, no icon dependency) — matches the
// convention in DrawToolbar.jsx / ThemeToggle.jsx.
const iconProps = {
  width: 14,
  height: 14,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  'aria-hidden': true,
}

const PointIcon = (
  <svg {...iconProps}>
    <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z" />
    <circle cx="12" cy="10" r="3" />
  </svg>
)
const LineIcon = (
  <svg {...iconProps}>
    <path d="M5 19L19 5" />
    <circle cx="5" cy="19" r="2" />
    <circle cx="19" cy="5" r="2" />
  </svg>
)
const PolygonIcon = (
  <svg {...iconProps}>
    <path d="M12 2l9.5 6.9-3.6 11.2H6.1L2.5 8.9 12 2z" />
  </svg>
)
const ProvinceIcon = (
  <svg {...iconProps}>
    <path d="M4 5.5 9 3l6 2.5L20 3v15.5L15 21l-6-2.5L4 21V5.5z" />
    <path d="M9 3v15.5M15 5.5V21" />
    <circle cx="12" cy="11.5" r="1.8" fill="currentColor" stroke="none" />
  </svg>
)

const LAYERS = [
  { type: 'point', label: 'Points', icon: PointIcon },
  { type: 'line', label: 'Lines', icon: LineIcon },
  { type: 'polygon', label: 'Polygons', icon: PolygonIcon },
  { type: 'province', label: 'Provinces & capitals', icon: ProvinceIcon },
]

// Layer visibility and the provenance-minded demo drawing legend. The toolbar embeds this panel in
// its Layers flyout; the `embedded` variant removes the legacy standalone positioning/chrome.
export default function LayerPanel({
  visibility,
  onToggle,
  embedded = false,
  displayOnly = false,
}) {
  const visibleLayers = visibleLayerEntries(LAYERS, displayOnly)

  return (
    <div
      className={`layer-panel${embedded ? ' layer-panel-embedded' : ''}`}
      role="group"
      aria-label="Layer visibility"
    >
      <span className="layer-panel-title">Layers</span>
      {displayOnly && (
        <p className="layer-panel-mode-note">
          WMS combines personal drawings into one server-rendered image.
        </p>
      )}
      {visibleLayers.map(({ type, label, icon }) => (
        <label key={type} className="layer-panel-row">
          <input
            type="checkbox"
            checked={visibility[type] ?? true}
            onChange={() => onToggle(type)}
          />
          <span className="layer-panel-icon" aria-hidden="true">
            {icon}
          </span>
          <span className="layer-panel-label">{label}</span>
        </label>
      ))}
      <details className="layer-panel-legend">
        <summary>Demo drawing themes</summary>
        <ul>
          {DEMO_DRAWING_THEMES.map(({ label, color }) => (
            <li key={label}>
              <span
                className="layer-panel-swatch"
                style={{ backgroundColor: color }}
                aria-hidden="true"
              />
              <span>{label}</span>
            </li>
          ))}
        </ul>
        <p>{DEMO_THEME_NOTE}</p>
      </details>
    </div>
  )
}
