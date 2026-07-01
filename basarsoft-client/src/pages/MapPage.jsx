import { useCallback, useEffect, useRef, useState } from 'react'
import Map from 'ol/Map'
import View from 'ol/View'
import Overlay from 'ol/Overlay'
import TileLayer from 'ol/layer/Tile'
import OSM from 'ol/source/OSM'
import VectorLayer from 'ol/layer/Vector'
import VectorSource from 'ol/source/Vector'
import Draw from 'ol/interaction/Draw'
import WKT from 'ol/format/WKT'
import { fromLonLat } from 'ol/proj'
import { Style, Circle as CircleStyle, Fill, Stroke } from 'ol/style'
import 'ol/ol.css'
import { useAuth } from '../context/auth-context'
import SessionTimer from '../components/SessionTimer'
import ThemeToggle from '../components/ThemeToggle'
import DrawToolbar from '../components/DrawToolbar'
import AttributeModal from '../components/AttributeModal'
import { listAllGeometry, saveGeometry, deleteGeometry } from '../api/geometry'
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

export default function MapPage() {
  const mapElementRef = useRef(null)
  const tooltipElementRef = useRef(null)
  const mapRef = useRef(null)
  const sourceRef = useRef(null)
  const statusTimerRef = useRef(null)
  const { logout } = useAuth()

  const [activeTool, setActiveTool] = useState('none')
  const [status, setStatus] = useState(null)
  // Holds a shape that was drawn but not yet confirmed: { feature, geomWkt, apiType }. When set,
  // the attribute popup is shown; the shape is only saved once the user clicks Save.
  const [pendingDraw, setPendingDraw] = useState(null)

  // Stable so effects can depend on it without re-running; shows a transient toast. `duration` lets
  // an important message (e.g. the polygon intersection result) linger longer so it isn't missed.
  const flashStatus = useCallback((type, text, duration = 2800) => {
    setStatus({ type, text })
    window.clearTimeout(statusTimerRef.current)
    statusTimerRef.current = window.setTimeout(() => setStatus(null), duration)
  }, [])

  // Create the map once and load the user's saved shapes.
  useEffect(() => {
    const source = new VectorSource()
    sourceRef.current = source

    const map = new Map({
      target: mapElementRef.current,
      layers: [
        new TileLayer({ source: new OSM() }),
        // Per-feature style so each shape renders in its own saved color.
        new VectorLayer({ source, style: (feature) => makeFeatureStyle(feature.get('color')) }),
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
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, [])

  // React to the selected tool: swap the Draw interaction, or wire up delete-on-click.
  useEffect(() => {
    const map = mapRef.current
    const source = sourceRef.current
    if (!map || !source) return undefined

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

    return undefined
  }, [activeTool, flashStatus])

  // Attribute popup — Save: persist the pending shape with its name + color.
  const handleModalSave = async (name, color) => {
    if (!pendingDraw) return
    const { feature, geomWkt, apiType } = pendingDraw
    try {
      const saved = await saveGeometry(apiType, geomWkt, name, color)
      feature.set('apiType', apiType)
      feature.set('dbId', saved.id)
      feature.set('name', name || null)
      feature.set('color', color || DEFAULT_COLOR)
      feature.changed() // re-run the layer style so the shape shows in its chosen color
      if (apiType === 'polygon' && saved.intersectionCount != null) {
        const n = saved.intersectionCount
        flashStatus('success', `Polygon saved. ${n} ${n === 1 ? 'inventory' : 'inventories'} inside.`, 6000)
      } else {
        flashStatus('success', `${apiType} saved.`)
      }
    } catch {
      sourceRef.current?.removeFeature(feature) // roll the drawing back if the save failed
      flashStatus('error', `Could not save ${apiType}.`)
    } finally {
      setPendingDraw(null)
    }
  }

  // Attribute popup — Cancel: throw the drawn shape away, nothing is saved.
  const handleModalCancel = () => {
    if (pendingDraw) sourceRef.current?.removeFeature(pendingDraw.feature)
    setPendingDraw(null)
  }

  return (
    <div className="map-page">
      <header className="map-bar">
        <div className="map-bar-left">
          <SessionTimer />
        </div>
        <span className="map-title">BasarsoftInternshipTask v0.0.3</span>
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
        {status && (
          <div className={`map-toast map-toast-${status.type}`} role="status">
            {status.text}
          </div>
        )}
      </div>
    </div>
  )
}
