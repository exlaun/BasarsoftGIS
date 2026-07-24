import { useEffect, useRef, useState } from 'react'
import Map from 'ol/Map'
import View from 'ol/View'
import TileLayer from 'ol/layer/Tile'
import OSM from 'ol/source/OSM'
import VectorLayer from 'ol/layer/Vector'
import VectorSource from 'ol/source/Vector'
import Draw from 'ol/interaction/Draw'
import Modify from 'ol/interaction/Modify'
import { fromLonLat } from 'ol/proj'
import { Style, Fill, Stroke } from 'ol/style'
import 'ol/ol.css'
import {
  getUserGeoArea,
  setUserGeoArea,
  clearUserGeoArea,
  getRoleGeoArea,
  setRoleGeoArea,
  clearRoleGeoArea,
} from '../../api/admin'
import {
  readAuthorizationAreaFeatures,
  writeAuthorizationAreaWkt,
} from './geoAuthorizationArea'
import ModalCloseButton from '../../components/ModalCloseButton'

// Same center as the main map; zoomed out so all of Turkey fits in the small modal map.
const TURKEY_CENTER = [35.2433, 38.9637]

// Green dashed = authorization boundary, matching how the restricted user sees it on the main map.
const AREA_STYLE = new Style({
  fill: new Fill({ color: 'rgba(22, 163, 74, 0.08)' }),
  stroke: new Stroke({ color: '#16a34a', width: 2, lineDash: [4, 8] }),
})

// Assign a geographic authorization area to a user or role on a small Turkey-zoomed map. Separate
// polygons remain separate editable components and are serialized together as a MultiPolygon.
export default function GeoAuthModal({ kind, targetId, targetLabel, onClose, onSuccess }) {
  const mapElRef = useRef(null)
  const mapRef = useRef(null)
  const sourceRef = useRef(null)
  const drawRef = useRef(null)
  const modifyRef = useRef(null)
  const [pendingWkt, setPendingWkt] = useState(null)
  const [hasExisting, setHasExisting] = useState(false)
  const [componentCount, setComponentCount] = useState(0)
  const [toolMode, setToolMode] = useState('draw')
  const [loading, setLoading] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')

  const isUser = kind === 'user'

  useEffect(() => {
    const onKey = (e) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  // Build the modal's own small map (the main map's OL instance lives only on /map): OSM tiles, one
  // vector layer for the area polygon, and a polygon Draw interaction that is always active.
  useEffect(() => {
    const source = new VectorSource()
    const map = new Map({
      target: mapElRef.current,
      layers: [
        new TileLayer({ source: new OSM() }),
        new VectorLayer({ source, style: AREA_STYLE }),
      ],
      view: new View({ center: fromLonLat(TURKEY_CENTER), zoom: 5.6 }),
    })
    mapRef.current = map
    sourceRef.current = source

    // The modal's layout settles after mount; without a deferred updateSize the canvas can size to 0.
    const sizeTimer = window.setTimeout(() => map.updateSize(), 0)

    const draw = new Draw({ source, type: 'Polygon' })
    const modify = new Modify({ source })
    draw.setActive(false)
    modify.setActive(false)
    drawRef.current = draw
    modifyRef.current = modify

    // Draw appends a new disconnected component. drawend fires before OpenLayers inserts the new
    // feature into the source, so include the event feature explicitly when serializing.
    draw.on('drawend', (event) => {
      const current = source.getFeatures()
      const next = current.includes(event.feature) ? current : [...current, event.feature]
      setPendingWkt(writeAuthorizationAreaWkt(next))
      setComponentCount(next.length)
      setError('')
    })
    modify.on('modifyend', () => {
      setPendingWkt(writeAuthorizationAreaWkt(source.getFeatures()))
      setError('')
    })
    map.addInteraction(draw)
    map.addInteraction(modify)

    // Show the currently assigned area (if any) and frame the view on it.
    let cancelled = false
    const getArea = isUser ? getUserGeoArea : getRoleGeoArea
    getArea(targetId)
      .then((area) => {
        if (cancelled) return
        if (area?.wkt) {
          const features = readAuthorizationAreaFeatures(area.wkt)
          source.addFeatures(features)
          map.getView().fit(source.getExtent(), {
            padding: [30, 30, 30, 30],
            maxZoom: 9,
          })
          setHasExisting(true)
          setComponentCount(features.length)
        }
        draw.setActive(true)
        setLoading(false)
      })
      .catch(() => {
        if (cancelled) return
        setError('Could not load the current area.')
        draw.setActive(true)
        setLoading(false)
      })

    return () => {
      cancelled = true
      window.clearTimeout(sizeTimer)
      map.setTarget(undefined)
      mapRef.current = null
      sourceRef.current = null
      drawRef.current = null
      modifyRef.current = null
    }
  }, [isUser, targetId])

  const selectTool = (mode) => {
    setToolMode(mode)
    drawRef.current?.setActive(mode === 'draw')
    modifyRef.current?.setActive(mode === 'edit')
  }

  const handleRemoveLast = () => {
    const source = sourceRef.current
    if (!source) return

    const features = source.getFeatures()
    const last = features.at(-1)
    if (!last) return

    source.removeFeature(last)
    const remaining = source.getFeatures()
    setPendingWkt(writeAuthorizationAreaWkt(remaining))
    setComponentCount(remaining.length)
    setError('')
  }

  const handleClearDrawing = () => {
    const source = sourceRef.current
    if (!source) return

    source.clear()
    setPendingWkt(null)
    setComponentCount(0)
    setError('')
  }

  const handleSave = async () => {
    if (!pendingWkt) return
    setSubmitting(true)
    setError('')
    try {
      await (isUser ? setUserGeoArea : setRoleGeoArea)(targetId, pendingWkt)
      onSuccess('Geographic area saved.')
    } catch (err) {
      setError(err.response?.status === 400 ? 'The drawn area is not a valid polygon.' : 'Could not save the area.')
      setSubmitting(false)
    }
  }

  const handleRemove = async () => {
    setSubmitting(true)
    setError('')
    try {
      await (isUser ? clearUserGeoArea : clearRoleGeoArea)(targetId)
      onSuccess('Geographic area removed.')
    } catch {
      setError('Could not remove the area.')
      setSubmitting(false)
    }
  }

  return (
    <div className="admin-modal-overlay" role="dialog" aria-modal="true" aria-label="Manage geographic area">
      <div className="admin-modal admin-modal-wide">
        <div className="admin-modal-head">
          <div>
            <h2 className="admin-modal-title">Geographic Access — {targetLabel}</h2>
            <p className="admin-modal-desc">
              Add one or more area components; {isUser ? 'this user' : 'users holding this role'} may
              only make changes inside their combined area. Disconnected components are preserved.
            </p>
          </div>
          <ModalCloseButton onClick={onClose} label="Close geographic access dialog" />
        </div>

        <div className="admin-modal-body">
          <div className="admin-geo-tools" role="toolbar" aria-label="Geographic area tools">
            <button
              type="button"
              className={`admin-btn${toolMode === 'draw' ? ' admin-btn-primary' : ''}`}
              aria-pressed={toolMode === 'draw'}
              onClick={() => selectTool('draw')}
              disabled={loading || submitting}
            >
              Add component
            </button>
            <button
              type="button"
              className={`admin-btn${toolMode === 'edit' ? ' admin-btn-primary' : ''}`}
              aria-pressed={toolMode === 'edit'}
              onClick={() => selectTool('edit')}
              disabled={loading || submitting || componentCount === 0}
            >
              Edit boundaries
            </button>
            <button
              type="button"
              className="admin-btn"
              onClick={handleRemoveLast}
              disabled={loading || submitting || componentCount === 0}
            >
              Remove last
            </button>
            <button
              type="button"
              className="admin-btn admin-btn-danger"
              onClick={handleClearDrawing}
              disabled={loading || submitting || componentCount === 0}
            >
              Clear map
            </button>
            <span className="admin-geo-count" role="status">
              {componentCount} {componentCount === 1 ? 'component' : 'components'}
            </span>
          </div>
          <div
            ref={mapElRef}
            className="admin-geo-map"
            role="region"
            aria-label="Authorization area drawing map"
          />
          <p className="admin-geo-hint">
            Add component appends a polygon. Edit boundaries moves vertices on any component. To
            replace everything, clear the map and draw again.
          </p>
          {loading && <p className="admin-loading">Loading current area…</p>}
          {error && <p className="admin-error">{error}</p>}
        </div>

        <div className="admin-modal-foot">
          {hasExisting && (
            <button type="button" className="admin-btn admin-btn-danger" onClick={handleRemove} disabled={submitting}>
              Remove area
            </button>
          )}
          <button
            type="button"
            className="admin-btn admin-btn-primary"
            onClick={handleSave}
            disabled={submitting || !pendingWkt}
          >
            Save
          </button>
        </div>
      </div>
    </div>
  )
}
