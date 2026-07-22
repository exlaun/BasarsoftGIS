import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import './DrawToolbar.css'

// Inline feather-style icons (inherit currentColor, no icon dependency) — matches the
// convention in ThemeToggle.jsx / PasswordInput.jsx. 18px reads well centred in a 2.5rem circle.
const iconProps = {
  width: 18,
  height: 18,
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
// Pencil — the Draw group parent (Point/Line/Polygon live under it).
const DrawIcon = (
  <svg {...iconProps}>
    <path d="M17 3a2.83 2.83 0 1 1 4 4L7.5 20.5 2 22l1.5-5.5L17 3z" />
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
// Pulse line — the Analysis group parent. Deliberately different from the bar-chart icon above so a
// collapsed group and its Intersection child never look like the same button.
const ActivityIcon = (
  <svg {...iconProps}>
    <polyline points="22 12 18 12 15 21 9 3 6 12 2 12" />
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
// Bus — add a transportation stop to a route. Distinct from the POI flag and the plain point pin.
const StopIcon = (
  <svg {...iconProps}>
    <rect x="4" y="4" width="16" height="13" rx="2" />
    <line x1="4" y1="11" x2="20" y2="11" />
    <line x1="8" y1="17" x2="8" y2="20" />
    <line x1="16" y1="17" x2="16" y2="20" />
    <circle cx="8" cy="14" r="1" />
    <circle cx="16" cy="14" r="1" />
  </svg>
)
// Pin with a plus — the Places group parent (POI/Add Stop): "put a shared thing on the map".
const PlacesIcon = (
  <svg {...iconProps}>
    <path d="M21 10c0 7-9 13-9 13s-9-6-9-13a9 9 0 0 1 18 0z" />
    <line x1="12" y1="7" x2="12" y2="13" />
    <line x1="9" y1="10" x2="15" y2="10" />
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

// The rail, top to bottom. Leaf `key` values are the tool keys MapPage expects: 'none' pans,
// 'select' inspects/edits/deletes, the three OL draw types draw, 'analysis' counts shapes under a
// temporary polygon. Deleting lives inside the Select popup (behind a confirmation) rather than as
// a click-to-delete tool.
//
// Related tools are collapsed behind one parent button whose flyout reveals the children — the two
// cursor modes stay top-level because they are the ones reached most often.
const TOOL_GROUPS = [
  { kind: 'tool', key: 'none', label: 'Pan', icon: PanIcon },
  { kind: 'tool', key: 'select', label: 'Select', icon: SelectIcon },
  {
    kind: 'group',
    id: 'draw',
    label: 'Draw',
    icon: DrawIcon,
    children: [
      { key: 'Point', label: 'Point', icon: PointIcon },
      { key: 'LineString', label: 'Line', icon: LineIcon },
      { key: 'Polygon', label: 'Polygon', icon: PolygonIcon },
    ],
  },
  {
    kind: 'group',
    id: 'places',
    label: 'Places',
    icon: PlacesIcon,
    children: [
      { key: 'Poi', label: 'POI', icon: PoiIcon },
      // Add a transportation stop (gated by manage_transport via TOOL_CONFIG, so End Users never see it).
      { key: 'AddStop', label: 'Add Stop', icon: StopIcon },
    ],
  },
  {
    kind: 'group',
    id: 'analysis',
    label: 'Analysis',
    icon: ActivityIcon,
    children: [
      { key: 'analysis', label: 'Intersection Analysis', icon: AnalysisIcon },
      { key: 'heatmap', label: 'Heat Map', icon: HeatmapIcon },
      // Weighted location analysis over POIs. No permission gate: like Analysis/Heat Map it is a
      // read-only tool, so the permission-free User (Viewer) role sees and uses it too.
      { key: 'konum', label: 'Location Analysis', icon: KonumIcon },
    ],
  },
  { kind: 'layers', id: 'layers', label: 'Layers', icon: LayersIcon },
]

// Hint pill (top-center) per active tool. Kept accurate to each interaction: Pan drags (no click), a
// Point finishes on a single click (no double-click), and only Line/Polygon need a double-click to finish.
const TOOL_HINTS = {
  none: 'Drag to move the map.',
  select: 'Click a shape to view its details.',
  Point: 'Click on the map to place a point.',
  LineString: 'Click to add points, then double-click to finish the line.',
  Polygon: 'Click to add corners, then double-click to finish the polygon.',
  Poi: 'Click on the map to place a POI, then fill in its details.',
  AddStop: 'Click on the map to place a stop, then choose its route.',
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
  // A tool the caller has no permission for is removed from the rail entirely (not just disabled), so
  // it never appears on that user's screen. A group left with no children disappears with them — a
  // Viewer holds no add_* permission, so Draw and Places vanish rather than opening onto nothing.
  // Pan/Select/Analysis/Layers carry no permission gate and always show.
  const visibleGroups = useMemo(
    () =>
      TOOL_GROUPS.map((entry) =>
        entry.kind === 'group'
          ? { ...entry, children: entry.children.filter((child) => !disabledTools.has(child.key)) }
          : entry,
      ).filter((entry) => entry.kind !== 'group' || entry.children.length > 0),
    [disabledTools],
  )

  // Id of the group whose flyout is open, or null. One at a time: opening a group replaces the
  // previous one. A click anywhere outside the rail, or Escape, closes it.
  const [openGroup, setOpenGroup] = useState(null)
  const railRef = useRef(null)
  useEffect(() => {
    if (!openGroup) return undefined
    const onDocPointerDown = (event) => {
      if (railRef.current && !railRef.current.contains(event.target)) setOpenGroup(null)
    }
    const onKeyDown = (event) => {
      if (event.key === 'Escape') setOpenGroup(null)
    }
    document.addEventListener('mousedown', onDocPointerDown)
    document.addEventListener('keydown', onKeyDown)
    return () => {
      document.removeEventListener('mousedown', onDocPointerDown)
      document.removeEventListener('keydown', onKeyDown)
    }
  }, [openGroup])

  // Picking a tool from a flyout closes it — the choice is made, and the map underneath is freed up.
  // The Layers flyout is deliberately excluded (see below): its rows are toggles, not a choice.
  const pickTool = useCallback(
    (key) => {
      onSelectTool(key)
      setOpenGroup(null)
    },
    [onSelectTool],
  )

  return (
    <div className="draw-toolbar">
      <p className="draw-toolbar-hint">{TOOL_HINTS[activeTool] ?? TOOL_HINTS.none}</p>

      <div className="draw-rail" ref={railRef}>
        {visibleGroups.map((entry) => {
          if (entry.kind === 'tool') {
            return (
              <div key={entry.key} className="draw-item">
                <button
                  type="button"
                  className={`draw-tool${activeTool === entry.key ? ' is-active' : ''}`}
                  onClick={() => pickTool(entry.key)}
                  aria-pressed={activeTool === entry.key}
                  aria-label={entry.label}
                >
                  <span className="draw-tool-icon">{entry.icon}</span>
                </button>
                <span className="draw-tip" role="tooltip">
                  {entry.label}
                </span>
              </div>
            )
          }

          const isOpen = openGroup === entry.id
          // A collapsed group still shows which of its tools is live, so the rail never hides the
          // current mode. Layers has no tool of its own — only its own open state.
          const isActive =
            entry.kind === 'group' && entry.children.some((child) => child.key === activeTool)

          return (
            <div
              key={entry.id}
              className={`draw-item${isOpen ? ' is-open' : ''}`}
            >
              <button
                type="button"
                className={`draw-tool${isActive || isOpen ? ' is-active' : ''}`}
                onClick={() => setOpenGroup((current) => (current === entry.id ? null : entry.id))}
                aria-haspopup="true"
                aria-expanded={isOpen}
                aria-label={entry.label}
              >
                <span className="draw-tool-icon">{entry.icon}</span>
                <span className="draw-tool-caret" aria-hidden="true" />
              </button>
              <span className="draw-tip" role="tooltip">
                {entry.label}
              </span>

              {isOpen && entry.kind === 'group' && (
                <div className="draw-flyout" role="group" aria-label={entry.label}>
                  {entry.children.map((child) => (
                    <div key={child.key} className="draw-item">
                      <button
                        type="button"
                        className={`draw-tool${activeTool === child.key ? ' is-active' : ''}`}
                        onClick={() => pickTool(child.key)}
                        aria-pressed={activeTool === child.key}
                        aria-label={child.label}
                      >
                        <span className="draw-tool-icon">{child.icon}</span>
                      </button>
                      <span className="draw-tip draw-tip-below" role="tooltip">
                        {child.label}
                      </span>
                    </div>
                  ))}
                </div>
              )}

              {/* Layer visibility: toggles rather than a choice, so this flyout stays open while the
                  user ticks several layers and closes only on outside-click/Escape. */}
              {isOpen && entry.kind === 'layers' && (
                <div className="draw-flyout" role="group" aria-label="Layer visibility">
                  {LAYER_OPTIONS.map(({ type, label, icon }) => {
                    const shown = layerVisibility[type] ?? true
                    return (
                      <div key={type} className="draw-item">
                        <button
                          type="button"
                          className={`draw-tool draw-layer-toggle${shown ? ' is-active' : ' is-off'}`}
                          onClick={() => onToggleLayer?.(type)}
                          aria-pressed={shown}
                          aria-label={`${label} layer`}
                        >
                          <span className="draw-tool-icon">{icon}</span>
                        </button>
                        <span className="draw-tip draw-tip-below" role="tooltip">
                          {label}
                        </span>
                      </div>
                    )
                  })}
                </div>
              )}
            </div>
          )
        })}
      </div>
    </div>
  )
}
