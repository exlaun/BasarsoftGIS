import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { useNavigate } from 'react-router-dom'
import Map from 'ol/Map'
import View from 'ol/View'
import Overlay from 'ol/Overlay'
import Collection from 'ol/Collection'
import Feature from 'ol/Feature'
import TileLayer from 'ol/layer/Tile'
import OSM from 'ol/source/OSM'
import VectorLayer from 'ol/layer/Vector'
import VectorSource from 'ol/source/Vector'
import ImageLayer from 'ol/layer/Image'
import ImageWMS from 'ol/source/ImageWMS'
import Draw from 'ol/interaction/Draw'
import Modify from 'ol/interaction/Modify'
import Translate from 'ol/interaction/Translate'
import WKT from 'ol/format/WKT'
import { fromLonLat } from 'ol/proj'
import { getCenter } from 'ol/extent'
import { Style, Circle as CircleStyle, Fill, Stroke, Text, Icon as IconStyle } from 'ol/style'
import 'ol/ol.css'
import { useAuth } from '../context/auth-context'
import SessionTimer from '../components/SessionTimer'
import ThemeToggle from '../components/ThemeToggle'
import DrawToolbar from '../components/DrawToolbar'
import AttributeModal from '../components/AttributeModal'
import ShapeInfoModal from '../components/ShapeInfoModal'
import ConfirmModal from '../components/ConfirmModal'
import ShapePickerModal from '../components/ShapePickerModal'
import QueryPanel from '../components/QueryPanel'
import InventoryInfoModal from '../components/InventoryInfoModal'
import PoiFormModal from '../components/PoiFormModal'
import PoiInfoModal from '../components/PoiInfoModal'
import PoiSearchBar from '../components/PoiSearchBar'
import PoiIconBadge from '../components/PoiIconBadge'
import LocationAnalysisPanel from '../components/LocationAnalysisPanel'
import { listAllGeometry, saveGeometry, updateGeometry, deleteGeometry, analyzeArea } from '../api/geometry'
import { listPois, createPoi, deletePoi, listPoiCategories } from '../api/poi'
import { listProvinces, getProvince, createLocationAnalysis } from '../api/locationAnalysis'
import {
  DEFAULT_POI_COLOR,
  categoryBadgeAppearance,
  normalizePoiIconKey,
  poiIconUrl,
} from '../utils/poiCategories'
import client, { getStoredAuth } from '../api/client'
import './MapPage.css'

// Approximate geographic center of Turkey (lon, lat).
const TURKEY_CENTER = [35.2433, 38.9637]

// The OSM view works in Web Mercator; we store WGS84 lon/lat in the DB. Transform at every boundary
// so a shape saved from the map lands back in exactly the same place when read.
const MAP_PROJ = 'EPSG:3857'
const DATA_PROJ = 'EPSG:4326'

// tool key -> OpenLayers draw type, backend {type} path segment, and required RBAC permission.
// 'poi' is not a /api/geometry/{type} — it saves through /api/poi — but sharing the shape keeps the
// draw-interaction and permission-gating plumbing identical for all four tools.
const TOOL_CONFIG = {
  Point: { drawType: 'Point', apiType: 'point', permission: 'add_point' },
  LineString: { drawType: 'LineString', apiType: 'line', permission: 'add_line' },
  Polygon: { drawType: 'Polygon', apiType: 'polygon', permission: 'add_polygon' },
  Poi: { drawType: 'Point', apiType: 'poi', permission: 'add_poi' },
}

const GEOMETRY_WRITE_PERMISSION_BY_TYPE = {
  point: 'add_point',
  line: 'add_line',
  polygon: 'add_polygon',
}

const wkt = new WKT()

// Fallback color for shapes drawn/saved before a color was chosen (older rows have color = null).
const DEFAULT_COLOR = '#2563eb'

// Build a style in the shape's own color: circle for points, stroke + translucent fill for lines/polygons.
// '26' is the hex alpha (~15%) appended to make an 8-digit hex the canvas understands.
const makeFeatureStyle = (color) => {
  const c = color || DEFAULT_COLOR
  return new Style({
    fill: new Fill({ color: c + '26' }),
    stroke: new Stroke({ color: c, width: 2 }),
    image: new CircleStyle({
      radius: 6,
      fill: new Fill({ color: c }),
      stroke: new Stroke({ color: '#ffffff', width: 1.5 }),
    }),
  })
}

// POI markers render in their category's color (inherited down the category tree, resolved
// server-side into categoryColor); a colorless category chain falls back to this rose, which no
// shape style uses — still instantly distinguishable from personal points.
const POI_COLOR = DEFAULT_POI_COLOR

// Labels appear below this view resolution (m/px). Mirrors MaxScaleDenominator 100000 in
// geoserver/styles/vw_poi_category.sld (100000 × 0.00028 m/px = 28): both display modes flip their
// POI labels at the same visual zoom (~12.5). Change the SLD and this constant together.
const POI_LABEL_MAX_RESOLUTION = 28

// One two-layer marker per color+icon pair, cached: a 26px category-colored circle underneath the
// fixed-white canonical SVG glyph. GeoServer packages the same SVGs for visually matched WMS output.
const poiMarkerStyles = new Map()
const poiMarkerStyle = (color, iconKey) => {
  const normalizedIconKey = normalizePoiIconKey(iconKey)
  const cacheKey = `${color}|${normalizedIconKey}`
  let styles = poiMarkerStyles.get(cacheKey)
  if (!styles) {
    // declutterMode 'none': the layer declutters (see its declutter flag), and without this the
    // circle claims its box first, so the overlapping glyph and name label of the SAME feature get
    // dropped as collisions. Markers must always draw; only labels compete for space (as in the SLD,
    // where conflictResolution sits on the TextSymbolizer alone).
    styles = [
      new Style({
        image: new CircleStyle({
          radius: 13,
          fill: new Fill({ color }),
          stroke: new Stroke({ color: '#ffffff', width: 2 }),
          declutterMode: 'none',
        }),
      }),
      new Style({
        image: new IconStyle({
          src: poiIconUrl(normalizedIconKey, import.meta.env.BASE_URL),
          width: 16,
          height: 16,
          declutterMode: 'none',
        }),
      }),
    ]
    poiMarkerStyles.set(cacheKey, styles)
  }
  return styles
}

// Category-colored marker; when zoomed in past the label threshold the POI's name renders above it
// (fresh text Style per call there — labeled views show at most a handful of POIs).
const poiStyleFn = (feature, resolution) => {
  const color = feature.get('categoryColor') || POI_COLOR
  const iconKey = feature.get('categoryIconKey')
  const name = feature.get('name')
  const markerStyles = poiMarkerStyle(color, iconKey)
  if (resolution >= POI_LABEL_MAX_RESOLUTION || !name) return markerStyles
  return [
    ...markerStyles,
    new Style({
      text: new Text({
        text: name,
        font: '600 12px sans-serif',
        offsetY: -23,
        fill: new Fill({ color: '#111827' }),
        stroke: new Stroke({ color: '#ffffff', width: 3 }),
      }),
    }),
  ]
}

// Distinct dashed style for the temporary analysis polygon so it reads as "not a saved shape".
const ANALYSIS_STYLE = new Style({
  fill: new Fill({ color: 'rgba(249, 115, 22, 0.12)' }),
  stroke: new Stroke({ color: '#f97316', width: 2, lineDash: [6, 6] }),
})

// The Konum Analizi target region (indigo, solid): visually distinct from both the orange dashed
// analysis polygon and the green dashed authorization boundary. The panel's region chip border
// (LocationAnalysisPanel.css) uses the same indigo so chip and outline read as one thing.
const KONUM_REGION_STYLE = new Style({
  fill: new Fill({ color: 'rgba(99, 102, 241, 0.08)' }),
  stroke: new Stroke({ color: '#6366f1', width: 2.5 }),
})

// The caller's geographic authorization boundary (green dashed, matching the admin modal's preview).
// Drawing outside this area is rejected — pre-checked here for instant feedback, enforced server-side.
const AUTH_AREA_STYLE = new Style({
  fill: new Fill({ color: 'rgba(22, 163, 74, 0.06)' }),
  stroke: new Stroke({ color: '#16a34a', width: 2, lineDash: [4, 8] }),
})

// Loader shared by every ImageWMS source that points at the backend proxy: an <img> can't carry the
// bearer token, so fetch with the Authorization header and hand OpenLayers a blob URL instead.
const loadAuthorizedWmsImage = (image, src) => {
  const token = getStoredAuth()?.token
  fetch(src, { headers: token ? { Authorization: `Bearer ${token}` } : {} })
    .then((res) => {
      if (!res.ok) throw new Error(`WMS ${res.status}`)
      return res.blob()
    })
    .then((blob) => {
      const objectUrl = URL.createObjectURL(blob)
      const img = image.getImage()
      img.addEventListener('load', () => URL.revokeObjectURL(objectUrl), { once: true })
      img.src = objectUrl
    })
    .catch(() => {
      // Leave the image unset (blank) on failure; the vector mode still works.
    })
}

export default function MapPage() {
  const mapElementRef = useRef(null)
  const tooltipElementRef = useRef(null)
  const mapRef = useRef(null)
  // One VectorSource/VectorLayer per geometry type so each can be shown/hidden independently
  // by the layer-control checkboxes. Keyed by apiType: { point, line, polygon }.
  const sourcesRef = useRef(null)
  const layersRef = useRef(null)
  // GeoServer WMS display layer + its source (for refresh()); hidden until the user picks WMS mode.
  const wmsLayerRef = useRef(null)
  const wmsSourceRef = useRef(null)
  // POI layer kept OUT of layersRef on purpose: the layer-control checkboxes only drive the three
  // shape layers. In WMS mode this overlay hides too — the WMS image already contains the POIs
  // (GeoServer renders vw_poi with its category style), so keeping it would double every marker.
  const poiLayerRef = useRef(null)
  // GeoServer heat map layer (vw_heat + vec:Heatmap style) + its source; visible only while the
  // 'heatmap' tool is active. Created once, so toggling can never stack duplicate layers.
  const heatLayerRef = useRef(null)
  const heatSourceRef = useRef(null)
  const analysisSourceRef = useRef(null)
  // Konum Analizi: the chosen target region outline (province boundary or drawn polygon), plus the
  // weighted heat map layer whose URL is re-pointed at /api/location-analysis/{id}/wms per run.
  // Same created-once rule as the heat layer, so re-running an analysis can't stack layers.
  const konumRegionSourceRef = useRef(null)
  const konumLayerRef = useRef(null)
  const konumSourceRef = useRef(null)
  // Geographic authorization boundary: its own source (for display) plus the raw OL geometry in map
  // projection (for the drawend inside-check without re-parsing WKT every draw).
  const authAreaSourceRef = useRef(null)
  const authGeomRef = useRef(null)
  const statusTimerRef = useRef(null)
  // Geometry captured before an edit so a cancelled edit can restore the shape's original position.
  const originalGeomRef = useRef(null)
  // Name/color carried from the info popup into geometry-edit mode, saved together with the new location.
  const pendingEditAttrsRef = useRef(null)
  const { logout, isAdmin, permissions, authorizedAreaWkt, userId } = useAuth()
  const navigate = useNavigate()

  const [activeTool, setActiveTool] = useState('none')
  const [status, setStatus] = useState(null)
  // Holds a shape that was drawn but not yet confirmed: { feature, geomWkt, apiType }. When set,
  // the attribute popup is shown; the shape is only saved once the user clicks Save.
  const [pendingDraw, setPendingDraw] = useState(null)
  // The saved shape currently selected for viewing/editing (Select tool): { feature } or null.
  const [selectedShape, setSelectedShape] = useState(null)
  // True while the delete-confirmation dialog is replacing the info popup for the selected shape.
  const [confirmingDelete, setConfirmingDelete] = useState(false)
  // The POI currently opened in its read-only info panel (Select tool): { feature } or null.
  // Separate from selectedShape so a POI can never wander into the shape edit/update paths.
  const [selectedPoi, setSelectedPoi] = useState(null)
  // True while the POI delete-confirmation dialog replaces the POI info panel.
  const [confirmingPoiDelete, setConfirmingPoiDelete] = useState(false)
  // Features stacked under one Select-tool click (2+): drives the which-shape chooser modal.
  const [overlapChoices, setOverlapChoices] = useState(null)
  // True while the selected shape's geometry is being dragged (Modify/Translate active).
  const [editingGeom, setEditingGeom] = useState(false)
  // Latest inventory-analysis result { points, lines, polygons, total }, or null. Persists until cleared.
  const [analysisResult, setAnalysisResult] = useState(null)
  // Which geometry types are visible on the map (layer-control checkboxes). All shown by default.
  const [layerVisibility, setLayerVisibility] = useState({ point: true, line: true, polygon: true })
  // Display source: 'vector' = editable WFS-backed vectors (default); 'wms' = flat GeoServer WMS image
  // (display-only, per the mentor's WMS-for-display / WFS-for-editing split).
  const [displayMode, setDisplayMode] = useState('vector')
  // Query-panel drawer open/closed.
  const [drawerOpen, setDrawerOpen] = useState(false)
  // Bumped after every successful create/update/move/delete so an open query panel refetches.
  const [refreshKey, setRefreshKey] = useState(0)
  // Saved POI features mirrored from the OL source into React state so an open search query reacts
  // to the async initial load and to create/delete operations without waiting for another keystroke.
  const [poiFeatures, setPoiFeatures] = useState([])
  // Read-only inventory info window (opened by the query panel's "i" button); null when closed.
  const [infoItem, setInfoItem] = useState(null)
  // --- Konum Analizi state ---
  // How the target region is being picked: 'province' (dropdown) or 'draw' (polygon on the map).
  const [konumRegionMode, setKonumRegionMode] = useState('province')
  // The chosen region: { label, provinceId } (dropdown) or { label, wkt } (drawn); null = none yet.
  const [konumRegion, setKonumRegion] = useState(null)
  // The running analysis (the POST response: id, criteria, matchedPoiCount, ...); null = form stage.
  const [konumAnalysis, setKonumAnalysis] = useState(null)
  // True while the create request is in flight (disables the Start button).
  const [konumRunning, setKonumRunning] = useState(false)
  // Dropdown/criteria reference data, fetched lazily the first time the tool is opened.
  const [provinces, setProvinces] = useState(null)
  const [konumCategories, setKonumCategories] = useState(null)

  const disabledDrawTools = useMemo(() => {
    const granted = new Set(permissions)
    return new Set(
      Object.entries(TOOL_CONFIG)
        .filter(([, config]) => !granted.has(config.permission))
        .map(([tool]) => tool),
    )
  }, [permissions])

  const canWriteShapeType = useCallback((type) => {
    const requiredPermission = GEOMETRY_WRITE_PERMISSION_BY_TYPE[type]
    return Boolean(requiredPermission && permissions.includes(requiredPermission))
  }, [permissions])

  const canWriteSelectedShape = Boolean(
    selectedShape && canWriteShapeType(selectedShape.feature.get('apiType')),
  )

  // Stable so effects can depend on it without re-running; shows a transient toast. `duration` lets
  // an important message linger longer so it isn't missed.
  const flashStatus = useCallback((type, text, duration = 2800) => {
    setStatus({ type, text })
    window.clearTimeout(statusTimerRef.current)
    statusTimerRef.current = window.setTimeout(() => setStatus(null), duration)
  }, [])

  // A permission can be revoked while the details or move UI is already open. Immediately return to
  // read-only details and restore any unsaved movement; the API independently enforces the same rule.
  useEffect(() => {
    if (!selectedShape || canWriteSelectedShape || (!confirmingDelete && !editingGeom)) return undefined

    const timer = window.setTimeout(() => {
      if (confirmingDelete) setConfirmingDelete(false)
      if (!editingGeom) return

      if (originalGeomRef.current) {
        selectedShape.feature.setGeometry(originalGeomRef.current)
      }
      setEditingGeom(false)
      originalGeomRef.current = null
      pendingEditAttrsRef.current = null
      flashStatus('error', 'Your permission changed. Unsaved shape edits were cancelled.')
    }, 0)

    return () => window.clearTimeout(timer)
  }, [
    canWriteSelectedShape,
    confirmingDelete,
    editingGeom,
    flashStatus,
    selectedShape,
  ])

  // Create the map once and load the user's saved shapes.
  useEffect(() => {
    // Per-feature style so each shape renders in its own saved color.
    const styleFn = (feature) => makeFeatureStyle(feature.get('color'))
    const sources = {
      point: new VectorSource(),
      line: new VectorSource(),
      polygon: new VectorSource(),
      // POIs share the sources map (so the Draw interaction's sources[apiType] lookup covers them)
      // but their layer deliberately lives outside `layers` — see poiLayerRef.
      poi: new VectorSource(),
    }
    const layers = {
      point: new VectorLayer({ source: sources.point, style: styleFn }),
      line: new VectorLayer({ source: sources.line, style: styleFn }),
      polygon: new VectorLayer({ source: sources.polygon, style: styleFn }),
    }
    sourcesRef.current = sources
    layersRef.current = layers
    // declutter mirrors the SLD's conflictResolution: overlapping labels drop instead of colliding.
    const poiLayer = new VectorLayer({ source: sources.poi, style: poiStyleFn, declutter: true })
    poiLayerRef.current = poiLayer
    const analysisSource = new VectorSource()
    analysisSourceRef.current = analysisSource
    const authAreaSource = new VectorSource()
    authAreaSourceRef.current = authAreaSource
    const konumRegionSource = new VectorSource()
    konumRegionSourceRef.current = konumRegionSource

    // GeoServer WMS display layer: a single rendered PNG of all the caller's shapes, fetched through
    // the backend (React -> /api/geometry/wms -> GeoServer). The backend fixes the layers and injects
    // uid from the JWT, so the browser only sends the viewport. An <img> can't carry the bearer token,
    // so a custom imageLoadFunction fetches with the Authorization header and hands OL a blob URL.
    const wmsSource = new ImageWMS({
      url: `${client.defaults.baseURL}/api/geometry/wms`,
      // LAYERS is required by OL but ignored server-side; VERSION 1.1.1 makes OL send SRS + x,y bbox.
      params: { LAYERS: 'basarsoft', FORMAT: 'image/png', TRANSPARENT: true, VERSION: '1.1.1' },
      ratio: 1,
      imageLoadFunction: loadAuthorizedWmsImage,
    })
    const wmsLayer = new ImageLayer({ source: wmsSource, visible: false })
    wmsSourceRef.current = wmsSource
    wmsLayerRef.current = wmsLayer

    // GeoServer heat map: same proxy pattern as the WMS display layer, but the backend fixes the
    // layer to vw_heat, whose default style is the vec:Heatmap rendering transformation.
    const heatSource = new ImageWMS({
      url: `${client.defaults.baseURL}/api/geometry/wms/heatmap`,
      params: { LAYERS: 'basarsoft', FORMAT: 'image/png', TRANSPARENT: true, VERSION: '1.1.1' },
      ratio: 1,
      imageLoadFunction: loadAuthorizedWmsImage,
    })
    const heatLayer = new ImageLayer({ source: heatSource, visible: false })
    heatSourceRef.current = heatSource
    heatLayerRef.current = heatLayer

    // Konum Analizi weighted heat map: same authorized-proxy pattern, but the URL is a placeholder
    // until an analysis is started — handleKonumStart re-points it at that run's id. Never visible
    // before then, so the placeholder is never actually requested.
    const konumSource = new ImageWMS({
      url: `${client.defaults.baseURL}/api/location-analysis/0/wms`,
      params: { LAYERS: 'basarsoft', FORMAT: 'image/png', TRANSPARENT: true, VERSION: '1.1.1' },
      ratio: 1,
      imageLoadFunction: loadAuthorizedWmsImage,
    })
    const konumLayer = new ImageLayer({ source: konumSource, visible: false })
    konumSourceRef.current = konumSource
    konumLayerRef.current = konumLayer

    const map = new Map({
      target: mapElementRef.current,
      layers: [
        new TileLayer({ source: new OSM() }),
        // GeoServer WMS display layer, above OSM. Hidden until the user switches to WMS mode.
        wmsLayer,
        // Authorization boundary sits under every shape layer: it's a backdrop, never clickable
        // (its feature carries no apiType/dbId, so select/hover logic skips it anyway).
        new VectorLayer({ source: authAreaSource, style: AUTH_AREA_STYLE }),
        // Konum Analizi target region: a backdrop like the auth boundary (no apiType/dbId either).
        new VectorLayer({ source: konumRegionSource, style: KONUM_REGION_STYLE }),
        // Polygons under lines under points, so small shapes stay clickable on top of large ones.
        layers.polygon,
        layers.line,
        layers.point,
        // POI markers above the personal shapes: they're the shared catalogue everyone must see.
        // Hidden in WMS mode, where GeoServer renders the same POIs inside the flat image.
        poiLayer,
        // Heat map raster above the shapes (its alpha ramp keeps them readable underneath), below the
        // temporary analysis polygon so that overlay always stays on top.
        heatLayer,
        // Konum Analizi weighted heat map: same raster slot as the plain heat map (the two are never
        // visible together — each follows its own tool).
        konumLayer,
        // Temporary analysis polygon lives on its own layer so it never mixes with saved shapes.
        new VectorLayer({ source: analysisSource, style: ANALYSIS_STYLE }),
      ],
      view: new View({
        center: fromLonLat(TURKEY_CENTER),
        zoom: 6.2,
      }),
    })
    mapRef.current = map

    // Hover tooltip: show a shape's saved name when the cursor is over it. Named features only.
    const tooltipEl = tooltipElementRef.current
    const tooltipOverlay = new Overlay({
      element: tooltipEl,
      positioning: 'bottom-center',
      offset: [0, -12],
      stopEvent: false,
    })
    map.addOverlay(tooltipOverlay)

    const onPointerMove = (event) => {
      if (event.dragging) return
      const feature = map.forEachFeatureAtPixel(event.pixel, (f) => f)
      const name = feature?.get('name')
      if (name) {
        tooltipEl.textContent = name
        tooltipEl.hidden = false
        tooltipOverlay.setPosition(event.coordinate)
      } else {
        tooltipEl.hidden = true
        tooltipOverlay.setPosition(undefined)
      }
      map.getTargetElement().style.cursor = feature ? 'pointer' : ''
    }
    map.on('pointermove', onPointerMove)

    // Read shapes back as WKT (in 4326) and transform into the map projection (3857) to render.
    listAllGeometry()
      .then((data) => {
        const groups = [
          ['point', data.points],
          ['line', data.lines],
          ['polygon', data.polygons],
        ]
        for (const [apiType, items] of groups) {
          for (const item of items ?? []) {
            const feature = wkt.readFeature(item.wkt, {
              dataProjection: DATA_PROJ,
              featureProjection: MAP_PROJ,
            })
            feature.set('apiType', apiType)
            feature.set('dbId', item.id)
            feature.set('name', item.name)
            feature.set('color', item.color)
            feature.set('modifiedDate', item.modifiedDate)
            feature.set('modifiedUserId', item.modifiedUserId)
            sources[apiType].addFeature(feature)
          }
        }
      })
      .catch(() => flashStatus('error', 'Could not load saved shapes.'))

    // POIs load independently of the shapes: /api/poi (GeoServer vw_poi, global for every user) is a
    // separate request from /api/geometry, so one source failing doesn't blank the other.
    listPois()
      .then((items) => {
        const loadedPois = []
        for (const item of items ?? []) {
          const feature = wkt.readFeature(item.wkt, {
            dataProjection: DATA_PROJ,
            featureProjection: MAP_PROJ,
          })
          feature.set('apiType', 'poi')
          feature.set('dbId', item.id)
          feature.set('name', item.name)
          feature.set('color', item.categoryColor || POI_COLOR) // for the overlap-picker swatch
          feature.set('categoryId', item.categoryId)
          feature.set('categoryName', item.categoryName)
          feature.set('categoryColor', item.categoryColor)
          feature.set('categoryIconKey', normalizePoiIconKey(item.categoryIconKey))
          feature.set('categoryPath', item.categoryPath)
          feature.set('openTime', item.openTime)
          feature.set('closeTime', item.closeTime)
          feature.set('createdBy', item.createdBy)
          feature.set('createdAt', item.createdAt)
          feature.set('userId', item.userId)
          loadedPois.push(feature)
        }
        sources.poi.addFeatures(loadedPois)
        setPoiFeatures(loadedPois)
      })
      .catch(() => flashStatus('error', 'Could not load POIs.'))

    return () => {
      map.un('pointermove', onPointerMove)
      map.setTarget(undefined)
      mapRef.current = null
      sourcesRef.current = null
      layersRef.current = null
      poiLayerRef.current = null
      wmsLayerRef.current = null
      wmsSourceRef.current = null
      heatLayerRef.current = null
      heatSourceRef.current = null
      analysisSourceRef.current = null
      authAreaSourceRef.current = null
      authGeomRef.current = null
      konumRegionSourceRef.current = null
      konumLayerRef.current = null
      konumSourceRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // Keep the authorization boundary in sync with the profile: it arrives async after login and can
  // change while the map is open (refreshProfile after an admin edits areas). Null = unrestricted,
  // so the layer is simply left empty.
  useEffect(() => {
    const source = authAreaSourceRef.current
    if (!source) return
    source.clear()
    authGeomRef.current = null
    if (!authorizedAreaWkt) return
    try {
      const geom = wkt.readGeometry(authorizedAreaWkt, {
        dataProjection: DATA_PROJ,
        featureProjection: MAP_PROJ,
      })
      authGeomRef.current = geom
      source.addFeature(new Feature(geom))
    } catch {
      // A malformed area must not break the map; the server still enforces on save.
    }
  }, [authorizedAreaWkt])

  // Apply the display mode + layer-control checkboxes. In WMS mode the vector layers are all hidden
  // (so nothing interactive dangles behind the flat image) and the WMS layer is shown + refreshed to
  // pull the latest render; in vector mode the WMS layer is hidden and each vector layer follows its
  // checkbox. Hidden layers keep their features but stop rendering, and OpenLayers' hit-testing skips
  // invisible layers automatically.
  useEffect(() => {
    const inWms = displayMode === 'wms'
    wmsLayerRef.current?.setVisible(inWms)
    for (const [type, layer] of Object.entries(layersRef.current ?? {})) {
      layer.setVisible(!inWms && layerVisibility[type])
    }
    // The WMS image includes the POIs (vw_poi is in the backend's GetMap layer list), so the client
    // POI overlay hides with the shape layers — otherwise every marker would render twice.
    poiLayerRef.current?.setVisible(!inWms)
    if (inWms) wmsSourceRef.current?.refresh()
  }, [layerVisibility, displayMode])

  // Heat map follows its tool: visible only while 'heatmap' is active, so picking any other tool (or
  // switching to WMS mode, which resets the tool to Pan) turns it off. Refreshed on every activation
  // so shapes drawn since the last look are counted in the density.
  useEffect(() => {
    const active = activeTool === 'heatmap'
    heatLayerRef.current?.setVisible(active)
    if (active) heatSourceRef.current?.refresh()
  }, [activeTool])

  // The Konum Analizi heat layer follows its tool AND a started run: no layer before Start, and
  // switching tools hides it (handleSelectTool also clears the run state, so this is belt-and-braces).
  useEffect(() => {
    konumLayerRef.current?.setVisible(activeTool === 'konum' && konumAnalysis != null)
  }, [activeTool, konumAnalysis])

  // Lazily fetch the Konum Analizi reference data (province list + category tree) the first time the
  // tool is opened; both are tiny and reused for every later run in the session.
  useEffect(() => {
    if (activeTool !== 'konum') return
    if (provinces == null) {
      listProvinces()
        .then(setProvinces)
        .catch(() => flashStatus('error', 'Could not load provinces.'))
    }
    if (konumCategories == null) {
      listPoiCategories()
        .then(setKonumCategories)
        .catch(() => flashStatus('error', 'Could not load categories.'))
    }
  }, [activeTool, provinces, konumCategories, flashStatus])

  const toggleLayer = (type) =>
    setLayerVisibility((prev) => ({ ...prev, [type]: !prev[type] }))

  // Drop every trace of the Konum Analizi tool: region outline, chosen region, running analysis.
  // Called when the user leaves the tool (other tool / WMS mode), so nothing stale lingers and
  // re-opening the tool starts fresh — the same "follows its tool" behavior as the heat map.
  const resetKonum = useCallback(() => {
    konumRegionSourceRef.current?.clear()
    setKonumRegion(null)
    setKonumAnalysis(null)
  }, [])

  // Switch between the editable vector display and the flat WMS image. Entering WMS drops any in-flight
  // editing state (active tool, selection, unsaved draw) so nothing is left hanging behind the hidden
  // vectors — WMS is display-only, exactly the mentor's "you can't operate on WMS" point.
  const handleDisplayMode = (mode) => {
    if (mode === 'wms') {
      setActiveTool('none')
      setSelectedShape(null)
      setEditingGeom(false)
      setConfirmingDelete(false)
      setSelectedPoi(null)
      setConfirmingPoiDelete(false)
      resetKonum()
      setPendingDraw((draw) => {
        if (draw) sourcesRef.current?.[draw.apiType].removeFeature(draw.feature)
        return null
      })
    }
    setDisplayMode(mode)
  }

  // Search-bar pick: fly to the POI and open its read-only info panel. Zoom 16 is deliberately past
  // the label threshold (~13.5), so the found POI arrives on screen with its name visible.
  const handlePoiSearchPick = useCallback((feature) => {
    if (editingGeom) return // don't hijack an in-progress geometry edit
    const view = mapRef.current?.getView()
    if (!view) return
    view.animate({ center: feature.getGeometry().getCoordinates(), zoom: 16, duration: 600 })
    setSelectedPoi({ feature })
  }, [editingGeom])

  // Route a clicked feature to the right panel: a POI opens its read-only info panel, a personal
  // shape the editable info popup. Used by both the single-hit click and the overlap picker.
  const openHit = useCallback((feature) => {
    if (feature.get('apiType') === 'poi') {
      setSelectedPoi({ feature })
    } else {
      setSelectedShape({ feature })
    }
  }, [])

  // Picking a draw tool re-enables that type's layer if it was unchecked, so a freshly
  // saved shape doesn't silently vanish into a hidden layer.
  const handleSelectTool = (tool) => {
    if (disabledDrawTools.has(tool)) {
      flashStatus('error', 'You do not have permission to use that draw tool.')
      return
    }
    if (tool !== 'konum') resetKonum()
    const apiType = TOOL_CONFIG[tool]?.apiType
    if (apiType) setLayerVisibility((prev) => (prev[apiType] ? prev : { ...prev, [apiType]: true }))
    setActiveTool(tool)
  }

  // If permissions are changed from the admin panel while a draw tool is active, drop back to Pan and
  // remove any unsaved shape that was waiting in the attribute modal.
  useEffect(() => {
    if (!disabledDrawTools.has(activeTool)) return undefined
    const timer = window.setTimeout(() => {
      setActiveTool('none')
      setPendingDraw((draw) => {
        if (draw) sourcesRef.current?.[draw.apiType].removeFeature(draw.feature)
        return null
      })
      flashStatus('error', 'Your drawing permission changed. The tool was disabled.')
    }, 0)
    return () => window.clearTimeout(timer)
  }, [activeTool, disabledDrawTools, flashStatus])

  // React to the selected tool: swap the Draw interaction, or wire up select / delete / analysis clicks.
  // While a geometry edit is in progress no tool interaction is added, so the Modify/Translate handles
  // (attached by the effect below) can't fight a draw/delete interaction.
  useEffect(() => {
    const map = mapRef.current
    const sources = sourcesRef.current
    if (!map || !sources || editingGeom) return undefined

    if (activeTool in TOOL_CONFIG) {
      if (disabledDrawTools.has(activeTool)) return undefined
      const { drawType, apiType } = TOOL_CONFIG[activeTool]
      const draw = new Draw({ source: sources[apiType], type: drawType })

      // On finish, don't save yet — capture the shape and open the attribute popup.
      draw.on('drawend', (event) => {
        const feature = event.feature
        const geometry = feature.getGeometry()

        // Geographic authorization pre-check: every vertex must fall inside the boundary. A vertex
        // test can miss an edge that dips outside between two inside vertices, so this is fast
        // feedback only — the server re-checks with full geometry containment on save.
        const authGeom = authGeomRef.current
        if (authGeom) {
          const geomType = geometry.getType()
          const vertices =
            geomType === 'Point'
              ? [geometry.getCoordinates()]
              : geomType === 'LineString'
                ? geometry.getCoordinates()
                : geometry.getCoordinates()[0] // Polygon outer ring
          if (vertices.some((coord) => !authGeom.intersectsCoordinate(coord))) {
            // drawend fires BEFORE OpenLayers adds the feature to the source, so remove on the
            // next tick — a synchronous removeFeature here would be a no-op.
            window.setTimeout(() => sourcesRef.current?.[apiType].removeFeature(feature), 0)
            flashStatus('error', 'The shape is outside your authorized area.')
            return
          }
        }

        // Transform from the map projection (3857) down to storage projection (4326) as WKT.
        const geomWkt = wkt.writeGeometry(geometry, {
          featureProjection: MAP_PROJ,
          dataProjection: DATA_PROJ,
        })
        setPendingDraw({ feature, geomWkt, apiType })
      })

      map.addInteraction(draw)
      return () => map.removeInteraction(draw)
    }

    if (activeTool === 'select') {
      const onClick = (event) => {
        // Collect EVERY saved shape under the click, not just the topmost: the callback returning
        // undefined tells forEachFeatureAtPixel to keep iterating (across all visible layers,
        // top-to-bottom), so stacked shapes all land in `hits`. Hidden layers are skipped by the
        // hit-test itself, and the analysis polygon fails the apiType/dbId guard.
        const hits = []
        map.forEachFeatureAtPixel(event.pixel, (f) => {
          if (f.get('apiType') && f.get('dbId') != null && !hits.includes(f)) hits.push(f)
        })
        if (hits.length === 0) return
        if (hits.length === 1) {
          openHit(hits[0])
          return
        }
        // Overlapping shapes: let the user pick which one to open.
        setOverlapChoices(hits)
      }
      map.on('singleclick', onClick)
      return () => {
        map.un('singleclick', onClick)
        setOverlapChoices(null) // no stale chooser if the tool changes mid-choice
      }
    }

    if (activeTool === 'konum') {
      // The Konum Analizi region draw only runs in draw mode and only until a run starts (the
      // region is frozen while its heat map is on screen; "New analysis" re-enables drawing).
      if (konumRegionMode !== 'draw' || konumAnalysis) return undefined
      const regionSource = konumRegionSourceRef.current
      const draw = new Draw({ source: regionSource, type: 'Polygon' })
      // One target region at a time: a new drawing replaces the previous outline.
      draw.on('drawstart', () => regionSource.clear())
      draw.on('drawend', (event) => {
        const geomWkt = wkt.writeGeometry(event.feature.getGeometry(), {
          featureProjection: MAP_PROJ,
          dataProjection: DATA_PROJ,
        })
        setKonumRegion({ label: 'Drawn region', wkt: geomWkt })
      })
      map.addInteraction(draw)
      return () => map.removeInteraction(draw)
    }

    if (activeTool === 'analysis') {
      const analysisSource = analysisSourceRef.current
      const draw = new Draw({ source: analysisSource, type: 'Polygon' })
      // One analysis polygon at a time: clear the previous one (and its result) when a new draw begins.
      draw.on('drawstart', () => {
        analysisSource.clear()
        setAnalysisResult(null)
      })
      draw.on('drawend', async (event) => {
        const geomWkt = wkt.writeGeometry(event.feature.getGeometry(), {
          featureProjection: MAP_PROJ,
          dataProjection: DATA_PROJ,
        })
        try {
          const result = await analyzeArea(geomWkt)
          setAnalysisResult(result)
        } catch {
          flashStatus('error', 'Could not run analysis.')
        }
      })
      map.addInteraction(draw)
      return () => map.removeInteraction(draw)
    }

    return undefined
  }, [activeTool, disabledDrawTools, editingGeom, flashStatus, openHit, konumRegionMode, konumAnalysis])

  // Geometry-edit mode: attach Modify (drag individual vertices) + Translate (drag the whole shape)
  // to just the selected feature. Both are removed when editing ends.
  //
  // OpenLayers dispatches pointer events to interactions in reverse add-order, so Modify (added last)
  // always sees a click before Translate does. By default Modify treats a drag anywhere along a
  // line/polygon segment as "insert a new vertex here and drag it" — for a Point or a Polygon's filled
  // interior that's rarely hit, but a LineString has no interior at all, so every drag on its body was
  // being claimed by Modify as a reshape, and Translate never got a turn to move the whole shape.
  // `insertVertexCondition: () => false` disables only that segment-click vertex insertion; dragging an
  // EXISTING vertex (hit-tested by pixelTolerance, unrelated to this condition) still reshapes exactly
  // as before. A plain drag on the shape's body now falls through to Translate instead. `hitTolerance`
  // gives Translate a forgiving pixel radius so grabbing a thin 2px line stroke doesn't require an
  // exact-pixel hit.
  useEffect(() => {
    const map = mapRef.current
    if (!map || !editingGeom || !selectedShape) return undefined

    const feature = selectedShape.feature
    const features = new Collection([feature])
    const translate = new Translate({ features, hitTolerance: 6 })
    const modify = new Modify({ features, insertVertexCondition: () => false })
    map.addInteraction(translate)
    map.addInteraction(modify)

    // Click-to-place: a single click AWAY from the shape moves it there (points land exactly on the
    // click; lines/polygons translate so their extent center does). This can't fight the drag paths:
    // OL only fires `singleclick` when the pointer did NOT move between down and up, so a Translate/
    // Modify drag never also triggers it. The one real overlap — clicking the shape itself without
    // dragging — is treated as a grab, not a jump, via the self-hit guard below (same 6px tolerance
    // as Translate, so "grabbable" and "ignored for placement" are the same area).
    const onClick = (event) => {
      const hitSelf = map.forEachFeatureAtPixel(
        event.pixel,
        (f) => (f === feature ? f : undefined),
        { hitTolerance: 6 },
      )
      if (hitSelf) return
      const geom = feature.getGeometry()
      if (geom.getType() === 'Point') {
        geom.setCoordinates(event.coordinate)
      } else {
        const [centerX, centerY] = getCenter(geom.getExtent())
        geom.translate(event.coordinate[0] - centerX, event.coordinate[1] - centerY)
      }
    }
    map.on('singleclick', onClick)

    return () => {
      map.un('singleclick', onClick)
      map.removeInteraction(modify)
      map.removeInteraction(translate)
    }
  }, [editingGeom, selectedShape])

  // Attribute popup (new draw) — Save: persist the pending shape with its name + color.
  const handleModalSave = async (name, color) => {
    if (!pendingDraw) return
    const { feature, geomWkt, apiType } = pendingDraw
    try {
      const saved = await saveGeometry(apiType, geomWkt, name, color)
      feature.set('apiType', apiType)
      feature.set('dbId', saved.id)
      feature.set('name', name || null)
      feature.set('color', color || DEFAULT_COLOR)
      feature.set('modifiedDate', saved.modifiedDate)
      feature.set('modifiedUserId', saved.modifiedUserId)
      feature.changed() // re-run the layer style so the shape shows in its chosen color
      setRefreshKey((key) => key + 1)
      if (apiType === 'polygon' && saved.intersectionCount != null) {
        const n = saved.intersectionCount
        flashStatus('success', `Polygon saved. ${n} ${n === 1 ? 'inventory' : 'inventories'} inside.`, 6000)
      } else {
        flashStatus('success', `${apiType} saved.`)
      }
    } catch (error) {
      sourcesRef.current?.[apiType].removeFeature(feature) // roll the drawing back if the save failed
      // The area rejection is also a 403, so the specific `code` must be checked first.
      const message = error.response?.data?.code === 'outside_authorized_area'
        ? 'The shape is outside your authorized area.'
        : error.response?.status === 403
          ? 'You do not have permission to save this shape type.'
          : `Could not save ${apiType}.`
      flashStatus('error', message)
    } finally {
      setPendingDraw(null)
    }
  }

  // Attribute popup (new draw) — Cancel: throw the drawn shape away, nothing is saved.
  const handleModalCancel = () => {
    if (pendingDraw) sourcesRef.current?.[pendingDraw.apiType].removeFeature(pendingDraw.feature)
    setPendingDraw(null)
  }

  // POI popup — Save: persist the placed point with its name, category, and working hours.
  // Mirrors handleModalSave (stamp the feature on success, roll the drawing back on failure).
  const handlePoiSave = async (name, categoryId, openTime, closeTime) => {
    if (!pendingDraw) return
    const { feature, geomWkt } = pendingDraw
    try {
      const saved = await createPoi(geomWkt, name, categoryId, openTime, closeTime)
      feature.set('apiType', 'poi')
      feature.set('dbId', saved.id)
      feature.set('name', saved.name)
      feature.set('color', saved.categoryColor || POI_COLOR)
      feature.set('categoryId', saved.categoryId)
      feature.set('categoryName', saved.categoryName)
      feature.set('categoryColor', saved.categoryColor)
      feature.set('categoryIconKey', normalizePoiIconKey(saved.categoryIconKey))
      feature.set('categoryPath', saved.categoryPath)
      feature.set('openTime', saved.openTime)
      feature.set('closeTime', saved.closeTime)
      feature.set('createdBy', saved.createdBy)
      feature.set('createdAt', saved.createdAt)
      feature.set('userId', saved.userId)
      setPoiFeatures((current) => current.includes(feature) ? current : [...current, feature])
      flashStatus('success', 'POI saved.')
    } catch (error) {
      sourcesRef.current?.poi.removeFeature(feature)
      const data = error.response?.data
      const message = data?.code === 'outside_authorized_area'
        ? 'The POI is outside your authorized area.'
        : data?.code === 'category_not_found'
          ? 'That category no longer exists. Pick another one.'
          : error.response?.status === 403
            ? 'You do not have permission to add POIs.'
            : 'Could not save the POI.'
      flashStatus('error', message)
    } finally {
      setPendingDraw(null)
    }
  }

  // POI confirm dialog — Delete: soft-delete on the server (creator or admin only), then drop the
  // marker from the map.
  const handlePoiDelete = async () => {
    if (!selectedPoi) return
    const { feature } = selectedPoi
    try {
      await deletePoi(feature.get('dbId'))
      sourcesRef.current?.poi.removeFeature(feature)
      setPoiFeatures((current) => current.filter((candidate) => candidate !== feature))
      flashStatus('success', 'POI deleted.')
      setSelectedPoi(null)
    } catch {
      flashStatus('error', 'Could not delete the POI.')
    } finally {
      setConfirmingPoiDelete(false)
    }
  }

  // Info popup — Save: persist edited name + color (geometry untouched).
  const handleInfoSave = async (name, color) => {
    if (!selectedShape) return
    const { feature } = selectedShape
    const apiType = feature.get('apiType')
    if (!canWriteShapeType(apiType)) {
      flashStatus('error', 'You do not have permission to update this shape.')
      return
    }
    const dbId = feature.get('dbId')
    try {
      const updated = await updateGeometry(apiType, dbId, { name, color })
      feature.set('name', name)
      feature.set('color', color)
      feature.set('modifiedDate', updated.modifiedDate)
      feature.set('modifiedUserId', updated.modifiedUserId)
      feature.changed()
      setRefreshKey((key) => key + 1)
      flashStatus('success', 'Shape updated.')
    } catch {
      flashStatus('error', 'Could not update shape.')
    } finally {
      setSelectedShape(null)
    }
  }

  // Info popup — Edit location: remember the (possibly edited) attributes + the original geometry, then
  // switch into geometry-drag mode. The attributes are saved together with the new location.
  const handleEditLocation = (name, color) => {
    if (!selectedShape) return
    if (!canWriteShapeType(selectedShape.feature.get('apiType'))) {
      flashStatus('error', 'You do not have permission to move this shape.')
      return
    }
    pendingEditAttrsRef.current = { name, color }
    originalGeomRef.current = selectedShape.feature.getGeometry().clone()
    setEditingGeom(true)
  }

  // Geometry-edit — Save: send the moved geometry (plus the carried name/color) to the server.
  const handleGeomSave = async () => {
    if (!selectedShape) return
    const { feature } = selectedShape
    const apiType = feature.get('apiType')
    if (!canWriteShapeType(apiType)) {
      if (originalGeomRef.current) feature.setGeometry(originalGeomRef.current)
      setEditingGeom(false)
      originalGeomRef.current = null
      pendingEditAttrsRef.current = null
      flashStatus('error', 'You do not have permission to move this shape.')
      return
    }
    const dbId = feature.get('dbId')
    const { name, color } = pendingEditAttrsRef.current ?? {
      name: feature.get('name'),
      color: feature.get('color'),
    }
    const geomWkt = wkt.writeGeometry(feature.getGeometry(), {
      featureProjection: MAP_PROJ,
      dataProjection: DATA_PROJ,
    })
    try {
      const updated = await updateGeometry(apiType, dbId, { wkt: geomWkt, name, color })
      feature.set('name', name)
      feature.set('color', color)
      feature.set('modifiedDate', updated.modifiedDate)
      feature.set('modifiedUserId', updated.modifiedUserId)
      feature.changed()
      setRefreshKey((key) => key + 1)
      flashStatus('success', 'Location updated.')
    } catch (error) {
      // Roll the shape back to where it was if the save failed.
      if (originalGeomRef.current) feature.setGeometry(originalGeomRef.current)
      flashStatus('error', error.response?.data?.code === 'outside_authorized_area'
        ? 'The new location is outside your authorized area.'
        : 'Could not update location.')
    } finally {
      setEditingGeom(false)
      setSelectedShape(null)
      originalGeomRef.current = null
      pendingEditAttrsRef.current = null
    }
  }

  // Geometry-edit — Cancel: restore the original geometry and drop out of edit mode.
  const handleGeomCancel = () => {
    if (selectedShape && originalGeomRef.current) {
      selectedShape.feature.setGeometry(originalGeomRef.current)
    }
    setEditingGeom(false)
    setSelectedShape(null)
    originalGeomRef.current = null
    pendingEditAttrsRef.current = null
  }

  // Info popup — Cancel: close without changes.
  const handleInfoCancel = () => {
    setSelectedShape(null)
    setConfirmingDelete(false)
  }

  // Confirm dialog — Delete: soft-delete on the server, then drop the feature from its layer.
  // On failure the selection is kept, so the info popup comes back as a natural retry path.
  const handleConfirmDelete = async () => {
    if (!selectedShape) return
    const { feature } = selectedShape
    const apiType = feature.get('apiType')
    if (!canWriteShapeType(apiType)) {
      setConfirmingDelete(false)
      flashStatus('error', 'You do not have permission to delete this shape.')
      return
    }
    const dbId = feature.get('dbId')
    try {
      await deleteGeometry(apiType, dbId)
      sourcesRef.current?.[apiType].removeFeature(feature)
      setRefreshKey((key) => key + 1)
      flashStatus('success', 'Shape deleted.')
      setSelectedShape(null)
    } catch {
      flashStatus('error', 'Could not delete shape.')
    } finally {
      setConfirmingDelete(false)
    }
  }

  // Analysis — Clear: remove the temporary polygon and hide the result panel.
  const handleClearAnalysis = () => {
    analysisSourceRef.current?.clear()
    setAnalysisResult(null)
  }

  // Konum Analizi — switch how the region is picked. Any half-chosen region is dropped so the
  // outline on the map always matches the mode the panel shows.
  const handleKonumRegionMode = (mode) => {
    setKonumRegionMode(mode)
    konumRegionSourceRef.current?.clear()
    setKonumRegion(null)
  }

  // Konum Analizi — province picked: fetch its boundary, outline it, and frame it in the visible
  // part of the map (the left padding clears the panel, like the query drawer's right padding).
  const handleKonumProvincePick = async (provinceId) => {
    try {
      const province = await getProvince(provinceId)
      const feature = wkt.readFeature(province.wkt, {
        dataProjection: DATA_PROJ,
        featureProjection: MAP_PROJ,
      })
      const source = konumRegionSourceRef.current
      source?.clear()
      source?.addFeature(feature)
      mapRef.current?.getView().fit(feature.getGeometry().getExtent(), {
        padding: [70, 70, 70, 420],
        maxZoom: 11,
        duration: 450,
      })
      setKonumRegion({ label: province.name, provinceId: province.id })
    } catch {
      flashStatus('error', 'Could not load the province boundary.')
    }
  }

  // Konum Analizi — clear the chosen region (outline + chip) but keep the criteria as typed.
  const handleKonumClearRegion = () => {
    konumRegionSourceRef.current?.clear()
    setKonumRegion(null)
  }

  // Konum Analizi — Start: create the run server-side (which re-validates every rule), then point
  // the weighted heat layer at that run's WMS proxy. The visibility effect shows the layer once
  // konumAnalysis lands in state.
  const handleKonumStart = async (criteria) => {
    if (!konumRegion || konumRunning) return
    setKonumRunning(true)
    try {
      const result = await createLocationAnalysis({
        provinceId: konumRegion.provinceId,
        regionWkt: konumRegion.wkt,
        criteria,
      })
      konumSourceRef.current?.setUrl(`${client.defaults.baseURL}/api/location-analysis/${result.id}/wms`)
      konumSourceRef.current?.refresh()
      setKonumAnalysis(result)
      if (result.matchedPoiCount === 0) {
        flashStatus('error', 'No POIs match the criteria inside this region — the heat map is empty.', 5000)
      } else {
        flashStatus('success', `Analysis started: ${result.matchedPoiCount} matching ${result.matchedPoiCount === 1 ? 'POI' : 'POIs'}.`, 4000)
      }
    } catch (error) {
      // One message per server rule (the `code` contract from LocationAnalysisController).
      const message = {
        region_required: 'Pick a province or draw a region first.',
        invalid_geometry: 'The drawn region is not a valid polygon.',
        province_not_found: 'That province no longer exists.',
        duplicate_category: 'Each category may be used in only one criterion.',
        category_not_found: 'A chosen category no longer exists. Pick another one.',
        weights_sum_invalid: 'Criterion weights must sum to exactly 100.',
      }[error.response?.data?.code] ?? 'Could not start the analysis.'
      flashStatus('error', message)
    } finally {
      setKonumRunning(false)
    }
  }

  // Konum Analizi — New analysis: drop the run (hides the heat map via the visibility effect) but
  // keep the chosen region + outline so weights can be retuned without re-picking the area.
  const handleKonumReset = () => setKonumAnalysis(null)

  // Query panel — row click: fly the map to that shape and open its info popup. The drawer stays
  // open underneath (the popup overlay has the higher z-index). The right padding keeps the shape
  // centered in the VISIBLE part of the map instead of under the 400px drawer.
  const handleRowClick = (item) => {
    if (editingGeom) return // don't hijack an in-progress geometry edit
    const feature = sourcesRef.current?.[item.type]
      ?.getFeatures()
      .find((f) => f.get('dbId') === item.id)
    if (!feature) {
      flashStatus('error', 'Shape is not on the map.')
      return
    }
    // Re-enable the type's layer if it was unchecked — zooming to an invisible shape helps no one.
    setLayerVisibility((prev) => (prev[item.type] ? prev : { ...prev, [item.type]: true }))
    const geom = feature.getGeometry()
    const view = mapRef.current.getView()
    if (geom.getType() === 'Point') {
      view.animate({ center: geom.getCoordinates(), zoom: 15, duration: 450 })
    } else {
      view.fit(geom.getExtent(), { padding: [90, 440, 90, 90], maxZoom: 16, duration: 450 })
    }
    setSelectedShape({ feature })
  }

  // Query panel — info ("i") button: open a read-only info window for that inventory. Reuses the same
  // (type, id) feature lookup as handleRowClick to pull the shape's coordinates + last editor off the
  // map; falls back to the row data if the feature isn't currently loaded. Does not zoom or edit.
  const handleInfoClick = (item) => {
    const feature = sourcesRef.current?.[item.type]
      ?.getFeatures()
      .find((f) => f.get('dbId') === item.id)
    let geomWkt = null
    let modifiedUserId = null
    if (feature) {
      geomWkt = wkt.writeGeometry(feature.getGeometry(), {
        featureProjection: MAP_PROJ,
        dataProjection: DATA_PROJ,
      })
      modifiedUserId = feature.get('modifiedUserId')
    }
    setInfoItem({
      id: item.id,
      type: item.type,
      name: item.name,
      color: item.color,
      createdAt: item.createdAt,
      modifiedDate: item.modifiedDate,
      modifiedUserId,
      wkt: geomWkt,
    })
  }

  return (
    <div className="map-page">
      <header className="map-bar">
        <div className="map-bar-left">
          <button className="map-logout" type="button" onClick={logout}>
            Logout
          </button>
          <SessionTimer />
        </div>
        <span className="map-title">BasarsoftGIS · Turkey Explorer</span>
        <div className="map-bar-right">
          <ThemeToggle />
          <span className="map-bar-divider" aria-hidden="true" />
          <div className="map-mode-toggle" role="group" aria-label="Display source">
            <button
              type="button"
              className={`map-mode-btn${displayMode === 'vector' ? ' active' : ''}`}
              onClick={() => handleDisplayMode('vector')}
              aria-pressed={displayMode === 'vector'}
              title="Editable WFS vector layer"
            >
              WFS
            </button>
            <button
              type="button"
              className={`map-mode-btn${displayMode === 'wms' ? ' active' : ''}`}
              onClick={() => handleDisplayMode('wms')}
              aria-pressed={displayMode === 'wms'}
              title="GeoServer WMS display layer"
            >
              WMS
            </button>
          </div>
          <span className="map-bar-divider" aria-hidden="true" />
          {isAdmin && (
            <button className="map-logout" type="button" onClick={() => navigate('/admin')}>
              Admin Panel
            </button>
          )}
          <button
            className="map-logout"
            type="button"
            onClick={() => setDrawerOpen((openNow) => !openNow)}
            aria-pressed={drawerOpen}
          >
            <svg
              width="14"
              height="14"
              viewBox="0 0 24 24"
              fill="none"
              stroke="currentColor"
              strokeWidth="2"
              strokeLinecap="round"
              strokeLinejoin="round"
              aria-hidden="true"
            >
              <path d="M8 6h13M8 12h13M8 18h13M3 6h.01M3 12h.01M3 18h.01" />
            </svg>{' '}
            Shapes
          </button>
        </div>
      </header>
      <div className="map-body">
        {displayMode === 'vector' ? (
          <DrawToolbar
            activeTool={activeTool}
            disabledTools={disabledDrawTools}
            onSelectTool={handleSelectTool}
            layerVisibility={layerVisibility}
            onToggleLayer={toggleLayer}
          />
        ) : (
          <div className="map-wms-hint" role="status">
            WMS display mode - shapes are served as images by GeoServer. Switch to WFS for drawing and
            editing.
          </div>
        )}
        <div ref={mapElementRef} className="map-container" />
        <div ref={tooltipElementRef} className="map-tooltip" hidden />

        {/* POI search: available to every role (the catalogue is shared and read is open) and in
            both display modes — it searches the loaded features, not the rendered pixels. */}
        <PoiSearchBar pois={poiFeatures} onPick={handlePoiSearchPick} />

        {/* Konum Analizi wizard: follows its toolbar tool (vector mode only — the toolbar itself is
            hidden in WMS mode and handleDisplayMode resets the tool). No permission gate: the
            assignment requires the plain User role to run analyses too. */}
        {displayMode === 'vector' && activeTool === 'konum' && (
          <LocationAnalysisPanel
            regionMode={konumRegionMode}
            onRegionModeChange={handleKonumRegionMode}
            provinces={provinces}
            region={konumRegion}
            onProvincePick={handleKonumProvincePick}
            onClearRegion={handleKonumClearRegion}
            categories={konumCategories}
            running={konumRunning}
            analysis={konumAnalysis}
            onStart={handleKonumStart}
            onReset={handleKonumReset}
          />
        )}

        <QueryPanel
          open={drawerOpen}
          refreshKey={refreshKey}
          onRowClick={handleRowClick}
          onInfoClick={handleInfoClick}
          onClose={() => setDrawerOpen(false)}
        />

        {pendingDraw && (
          pendingDraw.apiType === 'poi' ? (
            <PoiFormModal onSave={handlePoiSave} onCancel={handleModalCancel} />
          ) : (
            <AttributeModal onSave={handleModalSave} onCancel={handleModalCancel} />
          )
        )}
        {selectedShape && !editingGeom && (!confirmingDelete || !canWriteSelectedShape) && (
          <ShapeInfoModal
            type={selectedShape.feature.get('apiType')}
            initialName={selectedShape.feature.get('name')}
            initialColor={selectedShape.feature.get('color')}
            modifiedDate={selectedShape.feature.get('modifiedDate')}
            modifiedUserId={selectedShape.feature.get('modifiedUserId')}
            canEdit={canWriteSelectedShape}
            onSave={handleInfoSave}
            onEditLocation={handleEditLocation}
            onDelete={() => setConfirmingDelete(true)}
            onCancel={handleInfoCancel}
          />
        )}
        {selectedShape && confirmingDelete && canWriteSelectedShape && (
          <ConfirmModal
            title="Delete shape"
            message={`Delete "${selectedShape.feature.get('name') ?? 'this shape'}"? It will be hidden from the map but kept in the database (soft delete).`}
            confirmLabel="Delete"
            onConfirm={handleConfirmDelete}
            onCancel={() => setConfirmingDelete(false)}
          />
        )}
        {overlapChoices && (
          <ShapePickerModal
            features={overlapChoices}
            onPick={(feature) => {
              setOverlapChoices(null)
              openHit(feature)
            }}
            onCancel={() => setOverlapChoices(null)}
          />
        )}
        {selectedPoi && !confirmingPoiDelete && (
          <PoiInfoModal
            poi={{
              name: selectedPoi.feature.get('name'),
              categoryPath: selectedPoi.feature.get('categoryPath'),
              categoryColor: selectedPoi.feature.get('categoryColor') || POI_COLOR,
              categoryIconKey: selectedPoi.feature.get('categoryIconKey'),
              openTime: selectedPoi.feature.get('openTime'),
              closeTime: selectedPoi.feature.get('closeTime'),
              createdBy: selectedPoi.feature.get('createdBy'),
              createdAt: selectedPoi.feature.get('createdAt'),
            }}
            canDelete={isAdmin || (userId != null && selectedPoi.feature.get('userId') === userId)}
            onDelete={() => setConfirmingPoiDelete(true)}
            onClose={() => setSelectedPoi(null)}
          />
        )}
        {selectedPoi && confirmingPoiDelete && (
          <ConfirmModal
            title="Delete POI"
            message={`Delete POI "${selectedPoi.feature.get('name') ?? 'this POI'}"? It disappears from the map for everyone (soft delete).`}
            confirmLabel="Delete"
            onConfirm={handlePoiDelete}
            onCancel={() => setConfirmingPoiDelete(false)}
          />
        )}
        {infoItem && <InventoryInfoModal info={infoItem} onClose={() => setInfoItem(null)} />}

        {/* Heat map intensity legend, bottom-right, only while the heat map is shown. The gradient in
            MapPage.css mirrors the vw_heat_heatmap.sld ColorMap, so 0..1 here means the same thing
            GeoServer painted. */}
        {activeTool === 'heatmap' && (
          <div
            className="map-heat-legend"
            role="img"
            aria-label="Heat map intensity indicator: 0 low, 1 high"
          >
            <div className="map-heat-legend-title">Heat Map - Intensity</div>
            <div className="map-heat-legend-bar" aria-hidden="true" />
            <div className="map-heat-legend-scale">
              <span>0 - Low</span>
              <span>1 - High</span>
            </div>
          </div>
        )}

        {/* Konum Analizi legend: the same 0..1 gradient (vw_konum_heatmap.sld shares vw_heat's
            ColorMap) plus the run's criteria, each with its category color and weight — the
            "coloring per the given criteria" the assignment asks to see on screen. */}
        {activeTool === 'konum' && konumAnalysis && (
          <div className="map-heat-legend" role="group" aria-label="Location analysis legend">
            <div className="map-heat-legend-title">Konum Analizi - Weighted intensity</div>
            <div className="map-heat-legend-bar" aria-hidden="true" />
            <div className="map-heat-legend-scale">
              <span>0 - Low</span>
              <span>1 - High</span>
            </div>
            <ul className="map-konum-legend-criteria">
              {konumAnalysis.criteria.map((criterion) => (
                <li key={criterion.categoryId} className="map-konum-legend-row">
                  <PoiIconBadge
                    {...categoryBadgeAppearance(konumCategories ?? [], criterion.categoryId)}
                    size={18}
                    label={`${criterion.categoryName} marker`}
                  />
                  <span className="map-konum-legend-name">{criterion.categoryName}</span>
                  <span className="map-konum-legend-weight">{criterion.weight}</span>
                </li>
              ))}
            </ul>
          </div>
        )}

        {/* Shared bottom-center feedback column, anchored just above the toolbar. Analysis panel is the
            most long-lived so it sits furthest up when stacked; the toast is the most transient and
            keeps its established spot closest to the toolbar. */}
        <div className="map-status-stack">
          {analysisResult && (
            <div className="map-analysis-panel" role="status">
              <div className="map-analysis-total">
                {analysisResult.total} {analysisResult.total === 1 ? 'shape' : 'shapes'} in area
              </div>
              <div className="map-analysis-breakdown">
                {analysisResult.points} points · {analysisResult.lines} lines · {analysisResult.polygons} polygons
              </div>
              <button type="button" className="map-analysis-clear" onClick={handleClearAnalysis}>
                Clear
              </button>
            </div>
          )}

          {editingGeom && (
            <div className="map-edit-bar" role="status">
              <span className="map-edit-hint">
                Drag the shape or its points, or click the map to move it there, then save.
              </span>
              <div className="map-edit-actions">
                <button type="button" className="map-edit-btn map-edit-cancel" onClick={handleGeomCancel}>
                  Cancel
                </button>
                <button type="button" className="map-edit-btn map-edit-save" onClick={handleGeomSave}>
                  Save location
                </button>
              </div>
            </div>
          )}

          {status && (
            <div className={`map-toast map-toast-${status.type}`} role="status">
              {status.text}
            </div>
          )}
        </div>
      </div>
    </div>
  )
}
