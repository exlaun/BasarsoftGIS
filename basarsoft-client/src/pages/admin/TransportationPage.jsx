import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import {
  buildAdminRoute,
  deleteAdminRoute,
  deleteAdminStop,
  getAdminTransportation,
  reorderAdminStops,
  updateAdminRoute,
  updateAdminStop,
} from '../../api/transportation'
import { persistedRoutingMutation } from '../../utils/transportationMap'
import AdminConfirm from './AdminConfirm'

const DEFAULT_ROUTE_COLOR = '#2563eb'

function unpackSnapshot(snapshot) {
  return {
    routes: snapshot.routes.map((group) => group.route),
    stops: snapshot.routes.flatMap((group) => group.stops),
  }
}

function routeState(route) {
  if (route.isGeometryStale) return { label: 'Stale', className: 'stale' }
  if (route.geometryWkt) return { label: 'Ready', className: 'ready' }
  return { label: 'Not built', className: 'not-built' }
}

function formatDistance(meters) {
  return meters == null ? '—' : `${(meters / 1000).toFixed(1)} km`
}

function formatDuration(seconds) {
  if (seconds == null) return '—'
  const minutes = Math.round(seconds / 60)
  return minutes >= 60 ? `${Math.floor(minutes / 60)}h ${minutes % 60}m` : `${minutes} min`
}

function TransportationEditModal({ target, onClose, onSaved }) {
  const isRoute = target.type === 'route'
  // A route always has an effective color; a stop's is optional and null means "inherit the route's".
  // <input type="color"> has no null, so the inherit case needs its own flag — the picker still opens
  // on the inherited color so unchecking and rechecking doesn't jump to an unrelated swatch.
  const [name, setName] = useState(target.item.name ?? '')
  const [color, setColor] = useState(
    target.item.color || (isRoute ? DEFAULT_ROUTE_COLOR : target.item.routeColor || DEFAULT_ROUTE_COLOR),
  )
  const [overrideColor, setOverrideColor] = useState(isRoute || Boolean(target.item.color))
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const firstRef = useRef(null)

  useEffect(() => firstRef.current?.focus(), [])

  useEffect(() => {
    const onKeyDown = (event) => {
      if (event.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKeyDown)
    return () => window.removeEventListener('keydown', onKeyDown)
  }, [onClose])

  const handleSubmit = async (event) => {
    event.preventDefault()
    const trimmed = name.trim()
    if (!trimmed || submitting) return
    setSubmitting(true)
    setError('')
    try {
      const saved = isRoute
        ? await updateAdminRoute(target.item.id, { name: trimmed, color })
        : await updateAdminStop(target.item.id, { name: trimmed, color: overrideColor ? color : null })
      onSaved(target.type, saved)
    } catch (requestError) {
      setError(requestError.response?.data?.message ?? `Could not update the ${target.type}.`)
      setSubmitting(false)
    }
  }

  return (
    <div className="admin-modal-overlay" role="dialog" aria-modal="true" aria-label={`Edit ${target.type}`}>
      <form className="admin-modal" onSubmit={handleSubmit}>
        <div className="admin-modal-head">
          <h2 className="admin-modal-title">Edit {target.type}</h2>
          <p className="admin-modal-desc">Presentation-only changes do not rebuild road geometry.</p>
        </div>
        <div className="admin-modal-body">
          <label className="admin-field">
            <span>Name</span>
            <input
              ref={firstRef}
              type="text"
              value={name}
              maxLength={80}
              onChange={(event) => setName(event.target.value)}
            />
          </label>
          {!isRoute && (
            <label className="admin-field admin-field-check">
              <input
                type="checkbox"
                checked={overrideColor}
                onChange={(event) => setOverrideColor(event.target.checked)}
              />
              <span>Give this stop its own color</span>
            </label>
          )}
          {(isRoute || overrideColor) && (
            <label className="admin-field">
              <span>Color</span>
              <span className="admin-color-row">
                <input
                  type="color"
                  value={color}
                  onChange={(event) => setColor(event.target.value)}
                />
                <span className="admin-color-hint">{color}</span>
              </span>
            </label>
          )}
          {!isRoute && !overrideColor && (
            <p className="admin-modal-desc">
              Inherits its route&apos;s color, and follows it whenever the route is recolored.
            </p>
          )}
          {error && <p className="admin-error">{error}</p>}
        </div>
        <div className="admin-modal-foot">
          <button type="button" className="admin-btn" onClick={onClose}>Cancel</button>
          <button
            type="submit"
            className="admin-btn admin-btn-primary"
            disabled={!name.trim() || submitting}
          >
            {submitting ? 'Saving…' : 'Save'}
          </button>
        </div>
      </form>
    </div>
  )
}

export default function TransportationPage() {
  const [routes, setRoutes] = useState([])
  const [stops, setStops] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  const [editing, setEditing] = useState(null)
  // Pending delete awaiting confirmation: { type: 'route'|'stop', item } or null.
  const [deleting, setDeleting] = useState(null)
  const [busyRouteId, setBusyRouteId] = useState(null)
  const [toast, setToast] = useState(null)
  const toastTimer = useRef(null)

  const flash = useCallback((type, text, duration = 3000) => {
    setToast({ type, text })
    window.clearTimeout(toastTimer.current)
    toastTimer.current = window.setTimeout(() => setToast(null), duration)
  }, [])

  useEffect(() => () => window.clearTimeout(toastTimer.current), [])

  const load = useCallback(() => {
    setLoading(true)
    getAdminTransportation()
      .then((snapshot) => {
        const unpacked = unpackSnapshot(snapshot)
        setRoutes(unpacked.routes)
        setStops(unpacked.stops)
        setError(false)
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    getAdminTransportation()
      .then((snapshot) => {
        const unpacked = unpackSnapshot(snapshot)
        setRoutes(unpacked.routes)
        setStops(unpacked.stops)
        setError(false)
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false))
  }, [])

  const stopsByRoute = useMemo(() => {
    const grouped = new Map(routes.map((route) => [route.id, []]))
    for (const stop of stops) grouped.get(stop.routeId)?.push(stop)
    for (const routeStops of grouped.values()) {
      routeStops.sort((left, right) => left.sequenceOrder - right.sequenceOrder)
    }
    return grouped
  }, [routes, stops])

  const replaceRoute = useCallback((route) => {
    setRoutes((current) => current.map((item) => item.id === route.id ? route : item))
  }, [])

  const reconcileOrder = useCallback((routeId, payload) => {
    replaceRoute(payload.route)
    setStops((current) => [
      ...current.filter((stop) => stop.routeId !== routeId),
      ...payload.stops,
    ])
  }, [replaceRoute])

  const handleMove = async (route, routeStops, index, offset) => {
    const targetIndex = index + offset
    if (targetIndex < 0 || targetIndex >= routeStops.length || busyRouteId != null) return
    const next = [...routeStops]
    ;[next[index], next[targetIndex]] = [next[targetIndex], next[index]]
    setBusyRouteId(route.id)
    try {
      reconcileOrder(route.id, await reorderAdminStops(route.id, next.map((stop) => stop.id)))
      flash('success', 'Stop order saved and route rebuilt.')
    } catch (requestError) {
      const partial = persistedRoutingMutation(requestError, 'orderPersisted')
      if (partial) {
        reconcileOrder(route.id, partial)
        flash('error', requestError.response?.data?.message ?? 'Order saved, but routing failed.', 5000)
      } else {
        flash('error', requestError.response?.data?.message ?? 'Could not reorder stops.')
      }
    } finally {
      setBusyRouteId(null)
    }
  }

  const handleBuild = async (route) => {
    if (busyRouteId != null) return
    setBusyRouteId(route.id)
    try {
      replaceRoute(await buildAdminRoute(route.id))
      flash('success', 'Route geometry rebuilt.')
    } catch (requestError) {
      if (requestError.response?.data?.route) replaceRoute(requestError.response.data.route)
      flash('error', requestError.response?.data?.message ?? 'Could not rebuild the route.', 5000)
    } finally {
      setBusyRouteId(null)
    }
  }

  const handleSaved = (type, saved) => {
    if (type === 'route') replaceRoute(saved)
    else setStops((current) => current.map((stop) => stop.id === saved.id ? saved : stop))
    setEditing(null)
    flash('success', `${type === 'route' ? 'Route' : 'Stop'} updated.`)
  }

  // Confirmed deletion for both kinds. A route takes its stops with it, so both have to leave local
  // state together; a stop answers with its route's renumbered survivors, which reconcileOrder already
  // knows how to apply — the same payload a reorder returns.
  const handleDelete = async () => {
    const { type, item } = deleting
    setDeleting(null)
    try {
      if (type === 'route') {
        await deleteAdminRoute(item.id)
        setRoutes((current) => current.filter((route) => route.id !== item.id))
        setStops((current) => current.filter((stop) => stop.routeId !== item.id))
        flash('success', 'Route and its stops deleted.')
      } else {
        reconcileOrder(item.routeId, await deleteAdminStop(item.id))
        flash('success', 'Stop deleted and route rebuilt.')
      }
    } catch (requestError) {
      // The stop is gone even when the rebuild after it failed, so still reconcile — only warn.
      const partial = type === 'stop' && persistedRoutingMutation(requestError, 'deletePersisted')
      if (partial) {
        reconcileOrder(item.routeId, partial)
        flash('error', requestError.response?.data?.message ?? 'Stop deleted, but routing failed.', 5000)
        return
      }
      flash('error', requestError.response?.data?.message ?? `Could not delete the ${type}.`)
    }
  }

  return (
    <div>
      <div className="admin-page-head">
        <div>
          <h1 className="admin-page-title">Transportation</h1>
          <p className="admin-page-sub">
            Routes, ordered stops, and persisted OSRM road-geometry health.
          </p>
        </div>
        <button type="button" className="admin-btn" onClick={load} disabled={loading}>Refresh</button>
      </div>

      {loading ? (
        <div className="admin-card"><p className="admin-loading">Loading…</p></div>
      ) : error ? (
        <div className="admin-card"><p className="admin-empty">Could not load transportation data.</p></div>
      ) : routes.length === 0 ? (
        <div className="admin-card"><p className="admin-empty">No routes yet.</p></div>
      ) : (
        <div className="transport-admin-list">
          {routes.map((route) => {
            const routeStops = stopsByRoute.get(route.id) ?? []
            const state = routeState(route)
            const busy = busyRouteId === route.id
            return (
              <section key={route.id} className="admin-card transport-admin-route">
                <div className="transport-admin-route-head">
                  <span
                    className="transport-admin-swatch"
                    style={{ background: route.color || DEFAULT_ROUTE_COLOR }}
                    aria-hidden="true"
                  />
                  <div className="transport-admin-heading">
                    <h2>{route.name}</h2>
                    <span>{route.stopCount} {route.stopCount === 1 ? 'stop' : 'stops'}</span>
                  </div>
                  <div className="transport-admin-metrics">
                    <span className={`transport-admin-state is-${state.className}`}>{state.label}</span>
                    <span>{formatDistance(route.distanceMeters)}</span>
                    <span>{formatDuration(route.durationSeconds)}</span>
                    <span title={route.routingErrorCode || ''}>
                      Last error: {route.routingErrorCode || '—'}
                    </span>
                  </div>
                  <div className="admin-table-actions">
                    <button
                      type="button"
                      className="admin-btn admin-btn-sm"
                      onClick={() => setEditing({ type: 'route', item: route })}
                    >
                      Edit
                    </button>
                    <button
                      type="button"
                      className="admin-btn admin-btn-sm admin-btn-primary"
                      disabled={routeStops.length < 2 || busyRouteId != null}
                      onClick={() => handleBuild(route)}
                    >
                      {busy ? 'Working…' : 'Rebuild'}
                    </button>
                    <button
                      type="button"
                      className="admin-btn admin-btn-sm admin-btn-danger"
                      disabled={busyRouteId != null}
                      onClick={() => setDeleting({ type: 'route', item: route })}
                    >
                      Delete
                    </button>
                  </div>
                </div>

                {routeStops.length === 0 ? (
                  <p className="admin-empty">No stops on this route.</p>
                ) : (
                  <ol className="transport-admin-stops">
                    {routeStops.map((stop, index) => (
                      <li key={stop.id}>
                        <span
                          className="transport-admin-order"
                          style={{ background: stop.color || route.color || DEFAULT_ROUTE_COLOR }}
                        >
                          {stop.sequenceOrder}
                        </span>
                        <span className="transport-admin-stop-name">{stop.name || 'Unnamed'}</span>
                        <span className="admin-muted">Added by {stop.createdBy || 'unknown'}</span>
                        <div className="admin-table-actions">
                          <button
                            type="button"
                            className="admin-btn admin-btn-sm"
                            aria-label={`Move ${stop.name || 'stop'} up`}
                            disabled={index === 0 || busyRouteId != null}
                            onClick={() => handleMove(route, routeStops, index, -1)}
                          >
                            ↑
                          </button>
                          <button
                            type="button"
                            className="admin-btn admin-btn-sm"
                            aria-label={`Move ${stop.name || 'stop'} down`}
                            disabled={index === routeStops.length - 1 || busyRouteId != null}
                            onClick={() => handleMove(route, routeStops, index, 1)}
                          >
                            ↓
                          </button>
                          <button
                            type="button"
                            className="admin-btn admin-btn-sm"
                            onClick={() => setEditing({ type: 'stop', item: stop })}
                          >
                            Edit
                          </button>
                          <button
                            type="button"
                            className="admin-btn admin-btn-sm admin-btn-danger"
                            disabled={busyRouteId != null}
                            onClick={() => setDeleting({ type: 'stop', item: stop })}
                          >
                            Delete
                          </button>
                        </div>
                      </li>
                    ))}
                  </ol>
                )}
              </section>
            )
          })}
        </div>
      )}

      {editing && (
        <TransportationEditModal
          target={editing}
          onClose={() => setEditing(null)}
          onSaved={handleSaved}
        />
      )}
      {deleting && (
        <AdminConfirm
          title={deleting.type === 'route' ? 'Delete route' : 'Delete stop'}
          message={deleting.type === 'route'
            ? `Delete route "${deleting.item.name}" and its ${deleting.item.stopCount} ${deleting.item.stopCount === 1 ? 'stop' : 'stops'}? Everything disappears from the map for everyone (soft delete).`
            : `Delete stop "${deleting.item.name || 'Unnamed'}"? The route's remaining stops renumber and its road line is rebuilt without it.`}
          onConfirm={handleDelete}
          onCancel={() => setDeleting(null)}
        />
      )}
      {toast && <div className={`admin-toast admin-toast-${toast.type}`}>{toast.text}</div>}
    </div>
  )
}
