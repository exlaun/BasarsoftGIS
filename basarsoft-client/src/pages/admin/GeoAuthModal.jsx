import { useEffect, useRef, useState } from 'react'
import Map from 'ol/Map'
import View from 'ol/View'
import TileLayer from 'ol/layer/Tile'
import OSM from 'ol/source/OSM'
import VectorLayer from 'ol/layer/Vector'
import VectorSource from 'ol/source/Vector'
import Draw from 'ol/interaction/Draw'
import WKT from 'ol/format/WKT'
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

// Same center as the main map; zoomed out so all of Turkey fits in the small modal map.
const TURKEY_CENTER = [35.2433, 38.9637]
const MAP_PROJ = 'EPSG:3857'
const DATA_PROJ = 'EPSG:4326'

const wkt = new WKT()

// Green dashed = authorization boundary, matching how the restricted user sees it on the main map.
const AREA_STYLE = new Style({
  fill: new Fill({ color: 'rgba(22, 163, 74, 0.08)' }),
  stroke: new Stroke({ color: '#16a34a', width: 2, lineDash: [4, 8] }),
})

// Assign a geographic authorization area to a user or role: draw a polygon on a
// small Turkey-zoomed map; starting a new drawing replaces the previous polygon. The owner of the
// area may then only draw shapes inside it on the main map.
export default function GeoAuthModal({ kind, targetId, targetLabel, onClose, onSuccess }) {
  const mapElRef = useRef(null)
  const mapRef = useRef(null)
  const [pendingWkt, setPendingWkt] = useState(null)
  const [hasExisting, setHasExisting] = useState(false)
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

    // The modal's layout settles after mount; without a deferred updateSize the canvas can size to 0.
    const sizeTimer = window.setTimeout(() => map.updateSize(), 0)

    const draw = new Draw({ source, type: 'Polygon' })
    // One area at a time: a new drawing wipes the previous polygon (saved or pending).
    draw.on('drawstart', () => {
      source.clear()
      setPendingWkt(null)
    })
    draw.on('drawend', (event) => {
      const geomWkt = wkt.writeGeometry(event.feature.getGeometry(), {
        featureProjection: MAP_PROJ,
        dataProjection: DATA_PROJ,
      })
      setPendingWkt(geomWkt)
    })
    map.addInteraction(draw)

    // Show the currently assigned area (if any) and frame the view on it.
    let cancelled = false
    const getArea = isUser ? getUserGeoArea : getRoleGeoArea
    getArea(targetId)
      .then((area) => {
        if (cancelled) return
        if (area?.wkt) {
          const feature = wkt.readFeature(area.wkt, {
            dataProjection: DATA_PROJ,
            featureProjection: MAP_PROJ,
          })
          source.addFeature(feature)
          map.getView().fit(feature.getGeometry().getExtent(), {
            padding: [30, 30, 30, 30],
            maxZoom: 9,
          })
          setHasExisting(true)
        }
        setLoading(false)
      })
      .catch(() => {
        if (cancelled) return
        setError('Could not load the current area.')
        setLoading(false)
      })

    return () => {
      cancelled = true
      window.clearTimeout(sizeTimer)
      map.setTarget(undefined)
      mapRef.current = null
    }
  }, [isUser, targetId])

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
          <h2 className="admin-modal-title">Geographic Access — {targetLabel}</h2>
          <p className="admin-modal-desc">
            Draw a polygon; {isUser ? 'this user' : 'users holding this role'} will only be able to draw
            shapes inside it. Starting a new drawing replaces the previous area.
          </p>
        </div>

        <div className="admin-modal-body">
          <div ref={mapElRef} className="admin-geo-map" />
          {loading && <p className="admin-loading">Loading current area…</p>}
          {error && <p className="admin-error">{error}</p>}
        </div>

        <div className="admin-modal-foot">
          <button type="button" className="admin-btn" onClick={onClose}>
            Cancel
          </button>
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
