import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { listPois, deletePoi } from '../../api/poi'
import { formatWorkingHours } from '../../utils/poiCategories'
import AdminTable from '../../components/AdminTable'
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

  const poiColumns = useMemo(() => [
    { key: 'name', label: 'Name', sortValue: (poi) => poi.name, flex: 1, minWidth: 140 },
    {
      key: 'categoryPath',
      label: 'Category',
      sortValue: (poi) => poi.categoryPath,
      flex: 1.25,
      minWidth: 180,
      cellClassName: 'admin-wrap',
      render: (poi) => poi.categoryPath || <span className="admin-muted">—</span>,
    },
    {
      key: 'workingHours',
      label: 'Working hours',
      sortType: 'text',
      sortValue: (poi) => poi.openTime,
      flex: 0.95,
      minWidth: 140,
      render: (poi) => formatWorkingHours(poi.openTime, poi.closeTime),
    },
    {
      key: 'createdBy',
      label: 'Added by',
      sortValue: (poi) => poi.createdBy,
      flex: 0.8,
      minWidth: 120,
      render: (poi) => poi.createdBy || <span className="admin-muted">—</span>,
    },
    {
      key: 'createdAt',
      label: 'Added on',
      sortType: 'date',
      sortValue: (poi) => poi.createdAt,
      flex: 0.8,
      minWidth: 120,
      render: (poi) => new Date(poi.createdAt).toLocaleDateString(),
    },
    {
      key: 'actions',
      label: 'Actions',
      fixedWidth: 120,
      sortable: false,
      resizable: false,
      align: 'right',
      render: (poi) => (
        <div className="admin-table-actions">
          <button
            type="button"
            className="admin-btn admin-btn-sm admin-btn-danger"
            onClick={() => setModal({ type: 'delete', poi })}
          >
            Delete
          </button>
        </div>
      ),
    },
  ], [])

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
          <AdminTable
            columns={poiColumns}
            rows={pois}
            getRowKey={(poi) => poi.id}
            defaultSortKey="createdAt"
            defaultSortDir="desc"
          />
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
