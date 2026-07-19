import { useEffect, useRef, useState } from 'react'
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
// Bar chart — inventory analysis (count shapes under a temporary polygon).
const AnalysisIcon = (
  <svg {...iconProps}>
    <line x1="18" y1="20" x2="18" y2="10" />
    <line x1="12" y1="20" x2="12" y2="4" />
    <line x1="6" y1="20" x2="6" y2="14" />
  </svg>
)
// Flame — heat map analysis (density of the caller's shapes, rendered by GeoServer).
const HeatmapIcon = (
  <svg {...iconProps}>
    <path d="M12 22c4.4 0 7-2.8 7-6.5 0-2.5-1.2-4.6-2.8-6.3-.4 1-1 1.9-1.9 2.5C14.5 8.6 13.5 4.6 10 2c.3 2.6-.6 4.4-2 6-1.5 1.7-3 3.8-3 7 0 3.7 2.6 7 7 7z" />
  </svg>
)
// Flag — add a point of interest (POI). Distinct from the plain Point pin so operators can tell
// "draw my own shape" and "register a shared POI" apart at a glance.
const PoiIcon = (
  <svg {...iconProps}>
    <path d="M4 15s1-1 4-1 5 2 8 2 4-1 4-1V3s-1 1-4 1-5-2-8-2-4 1-4 1z" />
    <line x1="4" y1="22" x2="4" y2="15" />
  </svg>
)
// Crosshair — Konum Analizi (weighted location analysis over POIs in a chosen region).
const KonumIcon = (
  <svg {...iconProps}>
    <circle cx="12" cy="12" r="8" />
    <line x1="12" y1="1.5" x2="12" y2="5" />
    <line x1="12" y1="19" x2="12" y2="22.5" />
    <line x1="1.5" y1="12" x2="5" y2="12" />
    <line x1="19" y1="12" x2="22.5" y2="12" />
    <circle cx="12" cy="12" r="2.5" />
  </svg>
)
// Stacked sheets — toggles the layer-visibility menu.
const LayersIcon = (
  <svg {...iconProps}>
    <path d="M12 2L2 7l10 5 10-5-10-5z" />
    <path d="M2 17l10 5 10-5" />
    <path d="M2 12l10 5 10-5" />
  </svg>
)

// The three geometry layers the Layers menu can show/hide. Reuses the draw-tool icons above so a
// layer reads as the same thing as its draw tool.
const LAYER_OPTIONS = [
  { type: 'point', label: 'Points', icon: PointIcon },
  { type: 'line', label: 'Lines', icon: LineIcon },
  { type: 'polygon', label: 'Polygons', icon: PolygonIcon },
]

// Tool keys match what MapPage expects: 'none' pans, 'select' inspects/edits/deletes, the three OL
// draw types draw, 'analysis' counts shapes under a temporary polygon. Deleting lives inside the
// Select popup (behind a confirmation) rather than as a click-to-delete tool.
const TOOLS = [
  { key: 'none', label: 'Pan', icon: PanIcon },
  { key: 'select', label: 'Select', icon: SelectIcon },
  { key: 'Point', label: 'Point', icon: PointIcon },
  { key: 'LineString', label: 'Line', icon: LineIcon },
  { key: 'Polygon', label: 'Polygon', icon: PolygonIcon },
  { key: 'Poi', label: 'POI', icon: PoiIcon },
  { key: 'analysis', label: 'Analysis', icon: AnalysisIcon },
  { key: 'heatmap', label: 'Heat Map', icon: HeatmapIcon },
  // Weighted location analysis over POIs. No permission gate: like Analysis/Heat Map it is a
  // read-only tool, so the permission-free User (Viewer) role sees and uses it too.
  { key: 'konum', label: 'Location Analysis', icon: KonumIcon },
]

// Bottom-left hint per active tool. Kept accurate to each interaction: Pan drags (no click), a Point
// finishes on a single click (no double-click), and only Line/Polygon need a double-click to finish.
const TOOL_HINTS = {
  none: 'Drag to move the map.',
  select: 'Click a shape to view its details.',
  Point: 'Click on the map to place a point.',
  LineString: 'Click to add points, then double-click to finish the line.',
  Polygon: 'Click to add corners, then double-click to finish the polygon.',
  Poi: 'Click on the map to place a POI, then fill in its details.',
  analysis: 'Draw a polygon to count the shapes it touches. Nothing is saved.',
  heatmap: 'Shows your shape density as a GeoServer heat map. Pick another tool to turn it off.',
  konum: 'Pick a province or draw a region, weight 2-5 POI categories, then start the analysis.',
}

export default function DrawToolbar({
  activeTool,
  onSelectTool,
  disabledTools = new Set(),
  layerVisibility = {},
  onToggleLayer,
}) {
  // A tool the caller has no permission for is removed from the bar entirely (not just disabled), so
  // it never appears on that user's screen. Pan/Select/Analysis carry no permission gate and always show.
  const visibleTools = TOOLS.filter((tool) => !disabledTools.has(tool.key))

  // The Layers button opens an inline menu of the three visibility checkboxes. Kept open while the user
  // ticks boxes; a click anywhere outside closes it.
  const [layersOpen, setLayersOpen] = useState(false)
  const layersRef = useRef(null)
  useEffect(() => {
    if (!layersOpen) return undefined
    const onDocPointerDown = (event) => {
      if (layersRef.current && !layersRef.current.contains(event.target)) setLayersOpen(false)
    }
    document.addEventListener('mousedown', onDocPointerDown)
    return () => document.removeEventListener('mousedown', onDocPointerDown)
  }, [layersOpen])

  return (
    <div className="draw-toolbar">
      <p className="draw-toolbar-hint">{TOOL_HINTS[activeTool] ?? TOOL_HINTS.none}</p>

      <div className="draw-toolbar-tools">
        {visibleTools.map((tool) => (
          <button
            key={tool.key}
            type="button"
            className={`draw-tool${activeTool === tool.key ? ' is-active' : ''}`}
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

        <div className="draw-layers" ref={layersRef}>
          <button
            type="button"
            className={`draw-tool${layersOpen ? ' is-active' : ''}`}
            onClick={() => setLayersOpen((open) => !open)}
            aria-haspopup="true"
            aria-expanded={layersOpen}
            title="Layers"
          >
            <span className="draw-tool-icon" aria-hidden="true">
              {LayersIcon}
            </span>
            <span className="draw-tool-label">Layers</span>
          </button>

          {layersOpen && (
            <div className="draw-layers-menu" role="group" aria-label="Layer visibility">
              {LAYER_OPTIONS.map(({ type, label, icon }) => (
                <label key={type} className="draw-layers-row">
                  <input
                    type="checkbox"
                    checked={layerVisibility[type] ?? true}
                    onChange={() => onToggleLayer?.(type)}
                  />
                  <span className="draw-layers-icon" aria-hidden="true">
                    {icon}
                  </span>
                  <span>{label}</span>
                </label>
              ))}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
