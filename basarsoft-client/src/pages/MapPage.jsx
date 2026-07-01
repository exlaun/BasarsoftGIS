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

// One style covers all three geometry types: circle for points, stroke+fill for lines/polygons.
const featureStyle = new Style({
  fill: new Fill({ color: 'rgba(37, 99, 235, 0.15)' }),
  stroke: new Stroke({ color: '#2563eb', width: 2 }),
  image: new CircleStyle({
    radius: 6,
    fill: new Fill({ color: '#2563eb' }),
    stroke: new Stroke({ color: '#ffffff', width: 1.5 }),
  }),
})

export default function MapPage() {
  const mapElementRef = useRef(null)
  const tooltipElementRef = useRef(null)
  const mapRef = useRef(null)
  const sourceRef = useRef(null)
  const nameRef = useRef('')
  const statusTimerRef = useRef(null)
  const { logout } = useAuth()

  const [activeTool, setActiveTool] = useState('none')
  const [shapeName, setShapeName] = useState('')
  const [status, setStatus] = useState(null)

  // Keep the latest label in a ref so drawend (bound once per tool) always reads the current value.
  useEffect(() => {
    nameRef.current = shapeName
  }, [shapeName])

  // Stable so effects can depend on it without re-running; shows a transient toast.
  const flashStatus = useCallback((type, text) => {
    setStatus({ type, text })
    window.clearTimeout(statusTimerRef.current)
    statusTimerRef.current = window.setTimeout(() => setStatus(null), 2800)
  }, [])

  // Create the map once and load the user's saved shapes.
  useEffect(() => {
    const source = new VectorSource()
    sourceRef.current = source

    const map = new Map({
      target: mapElementRef.current,
      layers: [
        new TileLayer({ source: new OSM() }),
        new VectorLayer({ source, style: featureStyle }),
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

      draw.on('drawend', async (event) => {
        const feature = event.feature
        // Transform from the map projection (3857) down to storage projection (4326) as WKT.
        const geomWkt = wkt.writeGeometry(feature.getGeometry(), {
          featureProjection: MAP_PROJ,
          dataProjection: DATA_PROJ,
        })
        try {
          const saved = await saveGeometry(apiType, geomWkt, nameRef.current)
          feature.set('apiType', apiType)
          feature.set('dbId', saved.id)
          feature.set('name', nameRef.current || null)
          flashStatus('success', `${apiType} saved.`)
        } catch {
          source.removeFeature(feature) // roll the drawing back if the save failed
          flashStatus('error', `Could not save ${apiType}.`)
        }
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

  return (
    <div className="map-page">
      <header className="map-bar">
        <div className="map-bar-left">
          <SessionTimer />
        </div>
        <span className="map-title">BasarsoftInternshipTask v0.0.2</span>
        <div className="map-bar-right">
          <ThemeToggle />
          <button className="map-logout" type="button" onClick={logout}>
            Logout
          </button>
        </div>
      </header>
      <div className="map-body">
        <DrawToolbar
          activeTool={activeTool}
          onSelectTool={setActiveTool}
          shapeName={shapeName}
          onNameChange={setShapeName}
        />
        <div ref={mapElementRef} className="map-container" />
        <div ref={tooltipElementRef} className="map-tooltip" hidden />
        {status && (
          <div className={`map-toast map-toast-${status.type}`} role="status">
            {status.text}
          </div>
        )}
      </div>
    </div>
  )
}
