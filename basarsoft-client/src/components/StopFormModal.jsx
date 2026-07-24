import { useEffect, useRef, useState } from 'react'
import './AttributeModal.css'
import ModalCloseButton from './ModalCloseButton'
import { listRoutes } from '../api/transportation'

// Popup shown after the Add Stop tool places a point. Collects the stop's name and its route, then
// Save passes them up (the X discards the placed point). Modeled on PoiFormModal (name + a dropdown
// fetched fresh on open). When `lockedRoute` is given — adding a stop to a route already selected in
// the panel — the route is fixed: the dropdown shows only that route and is disabled.
export default function StopFormModal({ lockedRoute, onSave, onCancel }) {
  const [name, setName] = useState('')
  const [routeId, setRouteId] = useState(lockedRoute ? String(lockedRoute.id) : '')
  // null = loading; [] = none exist. Pre-filled with the single route when locked.
  const [routes, setRoutes] = useState(lockedRoute ? [lockedRoute] : null)
  const [submitting, setSubmitting] = useState(false)
  const nameInputRef = useRef(null)

  useEffect(() => {
    nameInputRef.current?.focus()
  }, [])

  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onCancel()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onCancel])

  // Fetch routes on open so a route just created in the panel is immediately pickable. Skipped when
  // the route is already fixed.
  useEffect(() => {
    if (lockedRoute) return undefined
    let cancelled = false
    listRoutes()
      .then((data) => {
        if (!cancelled) setRoutes(data)
      })
      .catch(() => {
        if (!cancelled) setRoutes([])
      })
    return () => {
      cancelled = true
    }
  }, [lockedRoute])

  const trimmedName = name.trim()
  const canSubmit = trimmedName.length > 0 && routeId !== '' && !submitting

  const handleSubmit = async (event) => {
    event.preventDefault()
    if (!canSubmit) return
    setSubmitting(true)
    // Save is async; the parent closes the modal on success and rolls the point back on failure, so
    // the spinner state here only needs to prevent double-submits.
    await onSave(trimmedName, Number(routeId))
  }

  return (
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="Stop details">
      <form className="attr-modal" onSubmit={handleSubmit}>
        <div className="attr-modal-head">
          <h2 className="attr-modal-title">Stop details</h2>
          <ModalCloseButton onClick={onCancel} label="Close stop details" />
        </div>

        <label className="attr-modal-field">
          <span>Name *</span>
          <input
            ref={nameInputRef}
            type="text"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="e.g. Central Station"
            maxLength={80}
          />
        </label>

        <label className="attr-modal-field">
          <span>Route *</span>
          <select
            value={routeId}
            onChange={(event) => setRouteId(event.target.value)}
            disabled={Boolean(lockedRoute) || routes === null}
          >
            <option value="" disabled>
              {routes === null
                ? 'Loading…'
                : routes.length === 0
                  ? 'No routes available'
                  : 'Choose a route'}
            </option>
            {(routes ?? []).map((route) => (
              <option key={route.id} value={route.id}>
                {route.name}
              </option>
            ))}
          </select>
        </label>

        {routes !== null && routes.length === 0 && !lockedRoute && (
          <p className="attr-modal-message">No routes yet — create a route first.</p>
        )}

        <div className="attr-modal-actions">
          <button type="submit" className="attr-modal-btn attr-modal-save" disabled={!canSubmit}>
            Save
          </button>
        </div>
      </form>
    </div>
  )
}
