import { useEffect, useRef } from 'react'
import Map from 'ol/Map'
import View from 'ol/View'
import TileLayer from 'ol/layer/Tile'
import OSM from 'ol/source/OSM'
import { fromLonLat } from 'ol/proj'
import 'ol/ol.css'
import { useAuth } from '../context/auth-context'
import SessionTimer from '../components/SessionTimer'
import './MapPage.css'

// Approximate geographic center of Turkey (lon, lat).
const TURKEY_CENTER = [35.2433, 38.9637]

export default function MapPage() {
  const mapElementRef = useRef(null)
  const { logout } = useAuth()

  useEffect(() => {
    const map = new Map({
      target: mapElementRef.current,
      layers: [new TileLayer({ source: new OSM() })],
      view: new View({
        center: fromLonLat(TURKEY_CENTER),
        zoom: 6.2,
      }),
    })

    // Detach OpenLayers from the DOM node on unmount.
    return () => map.setTarget(undefined)
  }, [])

  return (
    <div className="map-page">
      <header className="map-bar">
        <div className="map-bar-left">
          <SessionTimer />
        </div>
        <span className="map-title">BasarsoftInternshipTask v0.0.1</span>
        <div className="map-bar-right">
          <button className="map-logout" type="button" onClick={logout}>
            Logout
          </button>
        </div>
      </header>
      <div ref={mapElementRef} className="map-container" />
    </div>
  )
}
