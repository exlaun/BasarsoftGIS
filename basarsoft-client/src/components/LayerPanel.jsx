import './LayerPanel.css'

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

const LAYERS = [
  { type: 'point', label: 'Points', icon: PointIcon },
  { type: 'line', label: 'Lines', icon: LineIcon },
  { type: 'polygon', label: 'Polygons', icon: PolygonIcon },
]

// Floating layer-visibility control: one checkbox per geometry type. Unchecking hides that
// type's vector layer from the map; features are kept intact and reappear when re-checked.
export default function LayerPanel({ visibility, onToggle }) {
  return (
    <div className="layer-panel" role="group" aria-label="Layer visibility">
      <span className="layer-panel-title">Layers</span>
      {LAYERS.map(({ type, label, icon }) => (
        <label key={type} className="layer-panel-row">
          <input
            type="checkbox"
            checked={visibility[type]}
            onChange={() => onToggle(type)}
          />
          <span className="layer-panel-icon" aria-hidden="true">
            {icon}
          </span>
          <span className="layer-panel-label">{label}</span>
        </label>
      ))}
    </div>
  )
}
