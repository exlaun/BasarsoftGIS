import './DrawToolbar.css'

// Inline feather-style icons (inherit currentColor, no icon dependency) — matches the
// convention in ThemeToggle.jsx / PasswordInput.jsx.
const iconProps = {
  width: 16,
  height: 16,
  viewBox: '0 0 24 24',
  fill: 'none',
  stroke: 'currentColor',
  strokeWidth: 2,
  strokeLinecap: 'round',
  strokeLinejoin: 'round',
  'aria-hidden': true,
}

// Open hand — pan/move the map.
const PanIcon = (
  <svg {...iconProps}>
    <path d="M18 11V6a2 2 0 0 0-4 0M14 10V4a2 2 0 0 0-4 0v2M10 10.5V6a2 2 0 0 0-4 0v8" />
    <path d="M18 8a2 2 0 1 1 4 0v6a8 8 0 0 1-8 8h-2c-2.8 0-4.5-.86-5.99-2.34l-3.6-3.6a2 2 0 0 1 2.83-2.82L7 15" />
  </svg>
)
// Map pin — single point.
const PointIcon = (
  <svg {...iconProps}>
    <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z" />
    <circle cx="12" cy="10" r="3" />
  </svg>
)
// Polyline — connected segment with vertices.
const LineIcon = (
  <svg {...iconProps}>
    <path d="M5 19L19 5" />
    <circle cx="5" cy="19" r="2" />
    <circle cx="19" cy="5" r="2" />
  </svg>
)
// Pentagon outline — polygon.
const PolygonIcon = (
  <svg {...iconProps}>
    <path d="M12 2l9.5 6.9-3.6 11.2H6.1L2.5 8.9 12 2z" />
  </svg>
)
// Trash — delete a shape.
const DeleteIcon = (
  <svg {...iconProps}>
    <path d="M3 6h18M8 6V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2m3 0v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6" />
    <path d="M10 11v6M14 11v6" />
  </svg>
)

// Tool keys match what MapPage expects: 'none' pans, the three OL draw types draw, 'delete' removes.
const TOOLS = [
  { key: 'none', label: 'Pan', icon: PanIcon },
  { key: 'Point', label: 'Point', icon: PointIcon },
  { key: 'LineString', label: 'Line', icon: LineIcon },
  { key: 'Polygon', label: 'Polygon', icon: PolygonIcon },
  { key: 'delete', label: 'Delete', icon: DeleteIcon },
]

export default function DrawToolbar({ activeTool, onSelectTool, shapeName, onNameChange }) {
  return (
    <div className="draw-toolbar">
      <p className="draw-toolbar-hint">
        {activeTool === 'delete'
          ? 'Click a shape to remove it.'
          : activeTool === 'none'
            ? 'Pick a tool, then click the map.'
            : 'Click to draw. Double-click to finish lines/polygons.'}
      </p>

      <div className="draw-toolbar-tools">
        {TOOLS.map((tool) => (
          <button
            key={tool.key}
            type="button"
            className={`draw-tool${activeTool === tool.key ? ' is-active' : ''}${
              tool.key === 'delete' ? ' is-delete' : ''
            }`}
            onClick={() => onSelectTool(tool.key)}
            aria-pressed={activeTool === tool.key}
            title={tool.label}
          >
            <span className="draw-tool-icon" aria-hidden="true">
              {tool.icon}
            </span>
            <span className="draw-tool-label">{tool.label}</span>
          </button>
        ))}
      </div>

      <label className="draw-toolbar-name">
        <span>Name for new shapes</span>
        <input
          type="text"
          value={shapeName}
          onChange={(event) => onNameChange(event.target.value)}
          placeholder="e.g. Warehouse A"
          maxLength={80}
        />
      </label>
    </div>
  )
}
