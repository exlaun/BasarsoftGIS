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
// Cursor — select a shape to view/edit it.
const SelectIcon = (
  <svg {...iconProps}>
    <path d="M3 3l7.07 16.97 2.51-7.39 7.39-2.51L3 3z" />
    <path d="M13 13l6 6" />
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
// Bar chart — inventory analysis (count shapes under a temporary polygon).
const AnalysisIcon = (
  <svg {...iconProps}>
    <line x1="18" y1="20" x2="18" y2="10" />
    <line x1="12" y1="20" x2="12" y2="4" />
    <line x1="6" y1="20" x2="6" y2="14" />
  </svg>
)

// Tool keys match what MapPage expects: 'none' pans, 'select' inspects/edits, the three OL draw types
// draw, 'delete' removes, 'analysis' counts shapes under a temporary polygon.
const TOOLS = [
  { key: 'none', label: 'Pan', icon: PanIcon },
  { key: 'select', label: 'Select', icon: SelectIcon },
  { key: 'Point', label: 'Point', icon: PointIcon },
  { key: 'LineString', label: 'Line', icon: LineIcon },
  { key: 'Polygon', label: 'Polygon', icon: PolygonIcon },
  { key: 'delete', label: 'Delete', icon: DeleteIcon },
  { key: 'analysis', label: 'Analysis', icon: AnalysisIcon },
]

// Bottom-left hint per active tool. Kept accurate to each interaction: Pan drags (no click), a Point
// finishes on a single click (no double-click), and only Line/Polygon need a double-click to finish.
const TOOL_HINTS = {
  none: 'Drag to move the map.',
  select: 'Click a shape to view and edit it.',
  Point: 'Click on the map to place a point.',
  LineString: 'Click to add points, then double-click to finish the line.',
  Polygon: 'Click to add corners, then double-click to finish the polygon.',
  delete: 'Click a shape to remove it.',
  analysis: 'Draw a polygon to count the shapes it touches. Nothing is saved.',
}

export default function DrawToolbar({ activeTool, onSelectTool }) {
  return (
    <div className="draw-toolbar">
      <p className="draw-toolbar-hint">{TOOL_HINTS[activeTool] ?? TOOL_HINTS.none}</p>

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
    </div>
  )
}
