import { useCallback, useEffect, useRef, useState } from 'react'
import { listPois, deletePoi } from '../../api/poi'
import { formatTime } from '../../utils/poiCategories'
import AdminConfirm from './AdminConfirm'

// Read-only inventory of every POI in the system with who added it ("ekleyen"). POIs are created
// on the map by operators; here the admin can only inspect and remove them.
export default function PoisPage() {
  const [pois, setPois] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  const [modal, setModal] = useState(null) // { type: 'delete', poi }
  const [toast, setToast] = useState(null)
  const toastTimer = useRef()

  const flash = useCallback((type, text) => {
    setToast({ type, text })
    clearTimeout(toastTimer.current)
    toastTimer.current = setTimeout(() => setToast(null), 2600)
  }, [])

  useEffect(() => () => clearTimeout(toastTimer.current), [])

  const load = useCallback(() => {
    listPois()
      .then((data) => {
        setPois(data)
        setError(false)
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  const handleDelete = async () => {
    try {
      await deletePoi(modal.poi.id)
      setModal(null)
      flash('success', 'POI deleted.')
      load()
    } catch {
      setModal(null)
      flash('error', 'Could not delete POI.')
    }
  }

  return (
    <div>
      <div className="admin-page-head">
        <div>
          <h1 className="admin-page-title">POIs</h1>
          <p className="admin-page-sub">Every point of interest added on the map, and who added it.</p>
        </div>
      </div>

      <div className="admin-card">
        {loading ? (
          <p className="admin-loading">Loading…</p>
        ) : error ? (
          <p className="admin-empty">Could not load POIs.</p>
        ) : pois.length === 0 ? (
          <p className="admin-empty">No POIs yet. Operators add them with the map’s POI tool.</p>
        ) : (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>Category</th>
                  <th>Working hours</th>
                  <th>Added by</th>
                  <th>Added on</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {pois.map((p) => (
                  <tr key={p.id}>
                    <td>{p.name}</td>
                    <td className="admin-wrap">{p.categoryPath || <span className="admin-muted">—</span>}</td>
                    <td>
                      {formatTime(p.openTime)} – {formatTime(p.closeTime)}
                    </td>
                    <td>{p.createdBy || <span className="admin-muted">—</span>}</td>
                    <td>{new Date(p.createdAt).toLocaleDateString()}</td>
                    <td>
                      <div className="admin-table-actions">
                        <button type="button" className="admin-btn admin-btn-sm admin-btn-danger" onClick={() => setModal({ type: 'delete', poi: p })}>
                          Delete
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>

      {modal?.type === 'delete' && (
        <AdminConfirm
          title="Delete POI"
          message={`Delete POI "${modal.poi.name}"? It disappears from the map for everyone (soft delete).`}
          onConfirm={handleDelete}
          onCancel={() => setModal(null)}
        />
      )}

      {toast && <div className={`admin-toast admin-toast-${toast.type}`}>{toast.text}</div>}
    </div>
  )
}
