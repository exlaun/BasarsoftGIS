import { useCallback, useEffect, useRef, useState } from 'react'
import Map from 'ol/Map'
import View from 'ol/View'
import Overlay from 'ol/Overlay'
import Collection from 'ol/Collection'
import TileLayer from 'ol/layer/Tile'
import OSM from 'ol/source/OSM'
import VectorLayer from 'ol/layer/Vector'
import VectorSource from 'ol/source/Vector'
import Draw from 'ol/interaction/Draw'
import Modify from 'ol/interaction/Modify'
import Translate from 'ol/interaction/Translate'
import WKT from 'ol/format/WKT'
import { fromLonLat } from 'ol/proj'
import { Style, Circle as CircleStyle, Fill, Stroke } from 'ol/style'
import 'ol/ol.css'
import { useAuth } from '../context/auth-context'
import SessionTimer from '../components/SessionTimer'
import ThemeToggle from '../components/ThemeToggle'
import DrawToolbar from '../components/DrawToolbar'
import AttributeModal from '../components/AttributeModal'
import ShapeInfoModal from '../components/ShapeInfoModal'
import { listAllGeometry, saveGeometry, updateGeometry, deleteGeometry, analyzeArea } from '../api/geometry'
import './MapPage.css'

// Approximate geographic center of Turkey (lon, lat).
const TURKEY_CENTER = [35.2433, 38.9637]

// The OSM view works in Web Mercator; we store WGS84 lon/lat in the DB. Transform at every boundary
// so a shape saved from the map lands back in exactly the same place when read.
const MAP_PROJ = 'EPSG:3857'
const DATA_PROJ = 'EPSG:4326'

// tool key -> [OpenLayers draw type, backend {type} path segment].
const TOOL_CONFIG = {
  Point: { drawType: 'Point', apiType: 'point' },
  LineString: { drawType: 'LineString', apiType: 'line' },
  Polygon: { drawType: 'Polygon', apiType: 'polygon' },
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

export default function MapPage() {
  const mapElementRef = useRef(null)
  const tooltipElementRef = useRef(null)
  const mapRef = useRef(null)
  const sourceRef = useRef(null)
  const analysisSourceRef = useRef(null)
  const statusTimerRef = useRef(null)
  // Geometry captured before an edit so a cancelled edit can restore the shape's original position.
  const originalGeomRef = useRef(null)
  // Name/color carried from the info popup into geometry-edit mode, saved together with the new location.
  const pendingEditAttrsRef = useRef(null)
  const { logout } = useAuth()

  const [activeTool, setActiveTool] = useState('none')
  const [status, setStatus] = useState(null)
  // Holds a shape that was drawn but not yet confirmed: { feature, geomWkt, apiType }. When set,
  // the attribute popup is shown; the shape is only saved once the user clicks Save.
  const [pendingDraw, setPendingDraw] = useState(null)
  // The saved shape currently selected for viewing/editing (Select tool): { feature } or null.
  const [selectedShape, setSelectedShape] = useState(null)
  // True while the selected shape's geometry is being dragged (Modify/Translate active).
  const [editingGeom, setEditingGeom] = useState(false)
  // Latest inventory-analysis result { points, lines, polygons, total }, or null. Persists until cleared.
  const [analysisResult, setAnalysisResult] = useState(null)

  // Stable so effects can depend on it without re-running; shows a transient toast. `duration` lets
  // an important message linger longer so it isn't missed.
  const flashStatus = useCallback((type, text, duration = 2800) => {
    setStatus({ type, text })
    window.clearTimeout(statusTimerRef.current)
    statusTimerRef.current = window.setTimeout(() => setStatus(null), duration)
  }, [])

  // Create the map once and load the user's saved shapes.
  useEffect(() => {
    const source = new VectorSource()
    sourceRef.current = source
    const analysisSource = new VectorSource()
    analysisSourceRef.current = analysisSource

    const map = new Map({
      target: mapElementRef.current,
      layers: [
        new TileLayer({ source: new OSM() }),
        // Per-feature style so each shape renders in its own saved color.
        new VectorLayer({ source, style: (feature) => makeFeatureStyle(feature.get('color')) }),
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
            source.addFeature(feature)
          }
        }
      })
      .catch(() => flashStatus('error', 'Could not load saved shapes.'))

    return () => {
      map.un('pointermove', onPointerMove)
      map.setTarget(undefined)
      mapRef.current = null
      sourceRef.current = null
      analysisSourceRef.current = null
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // React to the selected tool: swap the Draw interaction, or wire up select / delete / analysis clicks.
  // While a geometry edit is in progress no tool interaction is added, so the Modify/Translate handles
  // (attached by the effect below) can't fight a draw/delete interaction.
  useEffect(() => {
    const map = mapRef.current
    const source = sourceRef.current
    if (!map || !source || editingGeom) return undefined

    if (activeTool in TOOL_CONFIG) {
      const { drawType, apiType } = TOOL_CONFIG[activeTool]
      const draw = new Draw({ source, type: drawType })

      // On finish, don't save yet — capture the shape and open the attribute popup.
      draw.on('drawend', (event) => {
        const feature = event.feature
        // Transform from the map projection (3857) down to storage projection (4326) as WKT.
        const geomWkt = wkt.writeGeometry(feature.getGeometry(), {
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
        const feature = map.forEachFeatureAtPixel(event.pixel, (f) => f)
        if (!feature) return
        const apiType = feature.get('apiType')
        const dbId = feature.get('dbId')
        if (!apiType || dbId == null) return // not a saved shape (e.g. the analysis polygon)
        setSelectedShape({ feature })
      }
      map.on('singleclick', onClick)
      return () => map.un('singleclick', onClick)
    }

    if (activeTool === 'delete') {
      const onClick = async (event) => {
        const feature = map.forEachFeatureAtPixel(event.pixel, (f) => f)
        if (!feature) return
        const apiType = feature.get('apiType')
        const dbId = feature.get('dbId')
        if (!apiType || dbId == null) return // an unsaved shape — nothing to delete server-side
        try {
          await deleteGeometry(apiType, dbId)
          source.removeFeature(feature)
          flashStatus('success', 'Shape deleted.')
        } catch {
          flashStatus('error', 'Could not delete shape.')
        }
      }
      map.on('singleclick', onClick)
      return () => map.un('singleclick', onClick)
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
  }, [activeTool, editingGeom, flashStatus])

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

    const features = new Collection([selectedShape.feature])
    const translate = new Translate({ features, hitTolerance: 6 })
    const modify = new Modify({ features, insertVertexCondition: () => false })
    map.addInteraction(translate)
    map.addInteraction(modify)

    return () => {
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
      feature.changed() // re-run the layer style so the shape shows in its chosen color
      flashStatus('success', `${apiType} saved.`)
    } catch {
      sourceRef.current?.removeFeature(feature) // roll the drawing back if the save failed
      flashStatus('error', `Could not save ${apiType}.`)
    } finally {
      setPendingDraw(null)
    }
  }

  // Attribute popup (new draw) — Cancel: throw the drawn shape away, nothing is saved.
  const handleModalCancel = () => {
    if (pendingDraw) sourceRef.current?.removeFeature(pendingDraw.feature)
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
      feature.changed()
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
      feature.changed()
      flashStatus('success', 'Location updated.')
    } catch {
      // Roll the shape back to where it was if the save failed.
      if (originalGeomRef.current) feature.setGeometry(originalGeomRef.current)
      flashStatus('error', 'Could not update location.')
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
  const handleInfoCancel = () => setSelectedShape(null)

  // Analysis — Clear: remove the temporary polygon and hide the result panel.
  const handleClearAnalysis = () => {
    analysisSourceRef.current?.clear()
    setAnalysisResult(null)
  }

  return (
    <div className="map-page">
      <header className="map-bar">
        <div className="map-bar-left">
          <SessionTimer />
        </div>
        <span className="map-title">BasarsoftInternshipTask v0.0.4</span>
        <div className="map-bar-right">
          <ThemeToggle />
          <button className="map-logout" type="button" onClick={logout}>
            Logout
          </button>
        </div>
      </header>
      <div className="map-body">
        <DrawToolbar activeTool={activeTool} onSelectTool={setActiveTool} />
        <div ref={mapElementRef} className="map-container" />
        <div ref={tooltipElementRef} className="map-tooltip" hidden />

        {pendingDraw && (
          <AttributeModal onSave={handleModalSave} onCancel={handleModalCancel} />
        )}
        {selectedShape && !editingGeom && (
          <ShapeInfoModal
            type={selectedShape.feature.get('apiType')}
            initialName={selectedShape.feature.get('name')}
            initialColor={selectedShape.feature.get('color')}
            modifiedDate={selectedShape.feature.get('modifiedDate')}
            onSave={handleInfoSave}
            onEditLocation={handleEditLocation}
            onCancel={handleInfoCancel}
          />
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
              <span className="map-edit-hint">Drag the shape or its points, then save.</span>
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
