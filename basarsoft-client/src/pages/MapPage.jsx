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
import Draw from 'ol/interaction/Draw'
import Modify from 'ol/interaction/Modify'
import Translate from 'ol/interaction/Translate'
import WKT from 'ol/format/WKT'
import { fromLonLat } from 'ol/proj'
import { getCenter } from 'ol/extent'
import { Style, Circle as CircleStyle, Fill, Stroke } from 'ol/style'
import 'ol/ol.css'
import { useAuth } from '../context/auth-context'
import SessionTimer from '../components/SessionTimer'
import ThemeToggle from '../components/ThemeToggle'
import DrawToolbar from '../components/DrawToolbar'
import LayerPanel from '../components/LayerPanel'
import AttributeModal from '../components/AttributeModal'
import ShapeInfoModal from '../components/ShapeInfoModal'
import ConfirmModal from '../components/ConfirmModal'
import ShapePickerModal from '../components/ShapePickerModal'
import QueryPanel from '../components/QueryPanel'
import InventoryInfoModal from '../components/InventoryInfoModal'
import { listAllGeometry, saveGeometry, updateGeometry, deleteGeometry, analyzeArea } from '../api/geometry'
import './MapPage.css'

// Approximate geographic center of Turkey (lon, lat).
const TURKEY_CENTER = [35.2433, 38.9637]

// The OSM view works in Web Mercator; we store WGS84 lon/lat in the DB. Transform at every boundary
// so a shape saved from the map lands back in exactly the same place when read.
const MAP_PROJ = 'EPSG:3857'
const DATA_PROJ = 'EPSG:4326'

// tool key -> OpenLayers draw type, backend {type} path segment, and required RBAC permission.
const TOOL_CONFIG = {
  Point: { drawType: 'Point', apiType: 'point', permission: 'add_point' },
  LineString: { drawType: 'LineString', apiType: 'line', permission: 'add_line' },
  Polygon: { drawType: 'Polygon', apiType: 'polygon', permission: 'add_polygon' },
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

// Distinct dashed style for the temporary analysis polygon so it reads as "not a saved shape".
const ANALYSIS_STYLE = new Style({
  fill: new Fill({ color: 'rgba(249, 115, 22, 0.12)' }),
  stroke: new Stroke({ color: '#f97316', width: 2, lineDash: [6, 6] }),
})

// The caller's geographic authorization boundary (green dashed, matching the admin modal's preview).
// Drawing outside this area is rejected — pre-checked here for instant feedback, enforced server-side.
const AUTH_AREA_STYLE = new Style({
  fill: new Fill({ color: 'rgba(22, 163, 74, 0.06)' }),
  stroke: new Stroke({ color: '#16a34a', width: 2, lineDash: [4, 8] }),
})

export default function MapPage() {
  const mapElementRef = useRef(null)
  const tooltipElementRef = useRef(null)
  const mapRef = useRef(null)
  // One VectorSource/VectorLayer per geometry type so each can be shown/hidden independently
  // by the layer-control checkboxes. Keyed by apiType: { point, line, polygon }.
  const sourcesRef = useRef(null)
  const layersRef = useRef(null)
  const analysisSourceRef = useRef(null)
  // Geographic authorization boundary: its own source (for display) plus the raw OL geometry in map
  // projection (for the drawend inside-check without re-parsing WKT every draw).
  const authAreaSourceRef = useRef(null)
  const authGeomRef = useRef(null)
  const statusTimerRef = useRef(null)
  // Geometry captured before an edit so a cancelled edit can restore the shape's original position.
  const originalGeomRef = useRef(null)
  // Name/color carried from the info popup into geometry-edit mode, saved together with the new location.
  const pendingEditAttrsRef = useRef(null)
  const { logout, isAdmin, permissions, authorizedAreaWkt } = useAuth()
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
  // Features stacked under one Select-tool click (2+): drives the which-shape chooser modal.
  const [overlapChoices, setOverlapChoices] = useState(null)
  // True while the selected shape's geometry is being dragged (Modify/Translate active).
  const [editingGeom, setEditingGeom] = useState(false)
  // Latest inventory-analysis result { points, lines, polygons, total }, or null. Persists until cleared.
  const [analysisResult, setAnalysisResult] = useState(null)
  // Which geometry types are visible on the map (layer-control checkboxes). All shown by default.
  const [layerVisibility, setLayerVisibility] = useState({ point: true, line: true, polygon: true })
  // Query-panel drawer open/closed.
  const [drawerOpen, setDrawerOpen] = useState(false)
  // Bumped after every successful create/update/move/delete so an open query panel refetches.
  const [refreshKey, setRefreshKey] = useState(0)
  // Read-only inventory info window (opened by the query panel's "i" button); null when closed.
  const [infoItem, setInfoItem] = useState(null)

  const disabledDrawTools = useMemo(() => {
    const granted = new Set(permissions)
    return new Set(
      Object.entries(TOOL_CONFIG)
        .filter(([, config]) => !granted.has(config.permission))
        .map(([tool]) => tool),
    )
  }, [permissions])

  // Stable so effects can depend on it without re-running; shows a transient toast. `duration` lets
  // an important message linger longer so it isn't missed.
  const flashStatus = useCallback((type, text, duration = 2800) => {
    setStatus({ type, text })
    window.clearTimeout(statusTimerRef.current)
    statusTimerRef.current = window.setTimeout(() => setStatus(null), duration)
  }, [])

  // Create the map once and load the user's saved shapes.
  useEffect(() => {
    // Per-feature style so each shape renders in its own saved color.
    const styleFn = (feature) => makeFeatureStyle(feature.get('color'))
    const sources = {
      point: new VectorSource(),
      line: new VectorSource(),
      polygon: new VectorSource(),
    }
    const layers = {
      point: new VectorLayer({ source: sources.point, style: styleFn }),
      line: new VectorLayer({ source: sources.line, style: styleFn }),
      polygon: new VectorLayer({ source: sources.polygon, style: styleFn }),
    }
    sourcesRef.current = sources
    layersRef.current = layers
    const analysisSource = new VectorSource()
    analysisSourceRef.current = analysisSource
    const authAreaSource = new VectorSource()
    authAreaSourceRef.current = authAreaSource

    const map = new Map({
      target: mapElementRef.current,
      layers: [
        new TileLayer({ source: new OSM() }),
        // Authorization boundary sits under every shape layer: it's a backdrop, never clickable
        // (its feature carries no apiType/dbId, so select/hover logic skips it anyway).
        new VectorLayer({ source: authAreaSource, style: AUTH_AREA_STYLE }),
        // Polygons under lines under points, so small shapes stay clickable on top of large ones.
        layers.polygon,
        layers.line,
        layers.point,
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

    return () => {
      map.un('pointermove', onPointerMove)
      map.setTarget(undefined)
      mapRef.current = null
      sourcesRef.current = null
      layersRef.current = null
      analysisSourceRef.current = null
      authAreaSourceRef.current = null
      authGeomRef.current = null
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

  // Apply the layer-control checkboxes: hidden layers keep their features but stop rendering,
  // and OpenLayers' hit-testing (hover/select) skips invisible layers automatically.
  useEffect(() => {
    for (const [type, layer] of Object.entries(layersRef.current ?? {})) {
      layer.setVisible(layerVisibility[type])
    }
  }, [layerVisibility])

  const toggleLayer = (type) =>
    setLayerVisibility((prev) => ({ ...prev, [type]: !prev[type] }))

  // Picking a draw tool re-enables that type's layer if it was unchecked, so a freshly
  // saved shape doesn't silently vanish into a hidden layer.
  const handleSelectTool = (tool) => {
    if (disabledDrawTools.has(tool)) {
      flashStatus('error', 'You do not have permission to use that draw tool.')
      return
    }
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
          setSelectedShape({ feature: hits[0] })
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
  }, [activeTool, disabledDrawTools, editingGeom, flashStatus])

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

  // Info popup — Save: persist edited name + color (geometry untouched).
  const handleInfoSave = async (name, color) => {
    if (!selectedShape) return
    const { feature } = selectedShape
    const apiType = feature.get('apiType')
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
    pendingEditAttrsRef.current = { name, color }
    originalGeomRef.current = selectedShape.feature.getGeometry().clone()
    setEditingGeom(true)
  }

  // Geometry-edit — Save: send the moved geometry (plus the carried name/color) to the server.
  const handleGeomSave = async () => {
    if (!selectedShape) return
    const { feature } = selectedShape
    const apiType = feature.get('apiType')
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
          <SessionTimer />
        </div>
        <span className="map-title">BasarsoftInternshipTask v0.0.7</span>
        <div className="map-bar-right">
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
          {isAdmin && (
            <button className="map-logout" type="button" onClick={() => navigate('/admin')}>
              Admin Panel
            </button>
          )}
          <ThemeToggle />
          <button className="map-logout" type="button" onClick={logout}>
            Logout
          </button>
        </div>
      </header>
      <div className="map-body">
        <DrawToolbar
          activeTool={activeTool}
          disabledTools={disabledDrawTools}
          onSelectTool={handleSelectTool}
        />
        <div ref={mapElementRef} className="map-container" />
        <div ref={tooltipElementRef} className="map-tooltip" hidden />

        {/* Hidden while a geometry edit is active — hiding the very layer being edited makes no sense. */}
        {!editingGeom && <LayerPanel visibility={layerVisibility} onToggle={toggleLayer} />}

        <QueryPanel
          open={drawerOpen}
          refreshKey={refreshKey}
          onRowClick={handleRowClick}
          onInfoClick={handleInfoClick}
          onClose={() => setDrawerOpen(false)}
        />

        {pendingDraw && (
          <AttributeModal onSave={handleModalSave} onCancel={handleModalCancel} />
        )}
        {selectedShape && !editingGeom && !confirmingDelete && (
          <ShapeInfoModal
            type={selectedShape.feature.get('apiType')}
            initialName={selectedShape.feature.get('name')}
            initialColor={selectedShape.feature.get('color')}
            modifiedDate={selectedShape.feature.get('modifiedDate')}
            modifiedUserId={selectedShape.feature.get('modifiedUserId')}
            onSave={handleInfoSave}
            onEditLocation={handleEditLocation}
            onDelete={() => setConfirmingDelete(true)}
            onCancel={handleInfoCancel}
          />
        )}
        {selectedShape && confirmingDelete && (
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
              setSelectedShape({ feature })
            }}
            onCancel={() => setOverlapChoices(null)}
          />
        )}
        {infoItem && <InventoryInfoModal info={infoItem} onClose={() => setInfoItem(null)} />}

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
