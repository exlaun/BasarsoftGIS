import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { listPoiCategories, deletePoiCategory } from '../../api/poi'
import { categoryBadgeAppearance, flattenCategoryTree } from '../../utils/poiCategories'
import PoiIconBadge from '../../components/PoiIconBadge'
import PoiCategoryFormModal from './PoiCategoryFormModal'
import AdminConfirm from './AdminConfirm'

// The POI category tree the operators' dropdown feeds from. Rows are the flat list re-ordered
// depth-first, indented per level so the parent-child structure is visible at a glance.
export default function PoiCategoriesPage() {
  const [categories, setCategories] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  const [modal, setModal] = useState(null) // { type: 'create'|'edit'|'delete', category? }
  const [toast, setToast] = useState(null)
  const toastTimer = useRef()

  const flash = useCallback((type, text) => {
    setToast({ type, text })
    clearTimeout(toastTimer.current)
    toastTimer.current = setTimeout(() => setToast(null), 2600)
  }, [])

  useEffect(() => () => clearTimeout(toastTimer.current), [])

  const load = useCallback(() => {
    listPoiCategories()
      .then((data) => {
        setCategories(data)
        setError(false)
      })
      .catch(() => setError(true))
      .finally(() => setLoading(false))
  }, [])

  useEffect(() => {
    load()
  }, [load])

  const tree = useMemo(() => flattenCategoryTree(categories), [categories])

  const closeAnd = (message) => {
    setModal(null)
    if (message) flash('success', message)
    load()
  }

  const handleDelete = async () => {
    try {
      await deletePoiCategory(modal.category.id)
      closeAnd('Category deleted.')
    } catch (err) {
      setModal(null)
      // 409 carries the reason (subcategories or POIs still attached) — show it verbatim.
      flash('error', err?.response?.data?.message ?? 'Could not delete category.')
    }
  }

  return (
    <div>
      <div className="admin-page-head">
        <div>
          <h1 className="admin-page-title">POI Categories</h1>
          <p className="admin-page-sub">
            The parent-child category tree operators pick from when adding a POI.
          </p>
        </div>
        <button type="button" className="admin-btn admin-btn-primary" onClick={() => setModal({ type: 'create' })}>
          + Add category
        </button>
      </div>

      <div className="admin-card">
        {loading ? (
          <p className="admin-loading">Loading…</p>
        ) : error ? (
          <p className="admin-empty">Could not load categories.</p>
        ) : tree.length === 0 ? (
          <p className="admin-empty">No categories yet. Create a top-level one (e.g. “Yeme İçme”) to start.</p>
        ) : (
          <div className="admin-table-wrap">
            <table className="admin-table">
              <thead>
                <tr>
                  <th>Name</th>
                  <th>POIs</th>
                  <th style={{ textAlign: 'right' }}>Actions</th>
                </tr>
              </thead>
              <tbody>
                {tree.map((c) => (
                  <tr key={c.id}>
                    <td>
                      <span className="admin-tree-name" style={{ paddingLeft: `${c.depth * 1.25}rem` }}>
                        {c.depth > 0 && <span className="admin-tree-branch" aria-hidden="true">└</span>}
                        <PoiIconBadge
                          {...categoryBadgeAppearance(categories, c.id)}
                          size={20}
                          label={`${c.name} effective marker${c.color == null || c.iconKey == null ? ' (partly inherited)' : ''}`}
                        />
                        {c.name}
                      </span>
                    </td>
                    <td>{c.poiCount}</td>
                    <td>
                      <div className="admin-table-actions">
                        <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'edit', category: c })}>
                          Edit
                        </button>
                        <button type="button" className="admin-btn admin-btn-sm admin-btn-danger" onClick={() => setModal({ type: 'delete', category: c })}>
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

      {(modal?.type === 'create' || modal?.type === 'edit') && (
        <PoiCategoryFormModal
          mode={modal.type}
          category={modal.category}
          categories={categories}
          onClose={() => setModal(null)}
          onSuccess={closeAnd}
        />
      )}
      {modal?.type === 'delete' && (
        <AdminConfirm
          title="Delete category"
          message={`Delete category "${modal.category.name}"? Categories with subcategories or POIs cannot be deleted.`}
          onConfirm={handleDelete}
          onCancel={() => setModal(null)}
        />
      )}

      {toast && <div className={`admin-toast admin-toast-${toast.type}`}>{toast.text}</div>}
    </div>
  )
}
