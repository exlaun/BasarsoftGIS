import { useCallback, useEffect, useMemo, useRef, useState } from 'react'
import { listPoiCategories, deletePoiCategory } from '../../api/poi'
import { categoryBadgeAppearance, flattenCategoryTree } from '../../utils/poiCategories'
import { compareAdminValues } from '../../utils/adminTableModel'
import PoiIconBadge from '../../components/PoiIconBadge'
import AdminTable from '../../components/AdminTable'
import PoiCategoryFormModal from './PoiCategoryFormModal'
import AdminConfirm from './AdminConfirm'

// The POI category tree the operators' dropdown feeds from. Rows are the flat list re-ordered
// depth-first, indented per level so the parent-child structure is visible at a glance.
function sortCategoryTree(rows, columns, sortKey, sortDir) {
  if (sortKey !== 'name' && sortKey !== 'poiCount') return rows
  const column = columns.find((item) => item.key === sortKey)
  if (!column) return rows

  const ids = new Set(rows.map((category) => category.id))
  const byParent = new Map()
  rows.forEach((category, index) => {
    const parentKey = category.parentId != null && ids.has(category.parentId) ? category.parentId : null
    const group = byParent.get(parentKey) ?? []
    group.push({ category, index })
    byParent.set(parentKey, group)
  })

  const direction = sortDir === 'desc' ? -1 : 1
  const ordered = []
  const walk = (parentKey) => {
    const group = byParent.get(parentKey) ?? []
    group.sort((left, right) => {
      const compared = compareAdminValues(
        column.sortValue(left.category),
        column.sortValue(right.category),
        column.sortType,
      )
      return compared === 0 ? left.index - right.index : compared * direction
    })
    group.forEach(({ category }) => {
      ordered.push(category)
      walk(category.id)
    })
  }
  walk(null)
  return ordered
}

export default function PoiCategoriesPage() {
  const [categories, setCategories] = useState([])
  const [loading, setLoading] = useState(true)
  const [error, setError] = useState(false)
  const [modal, setModal] = useState(null) // { type: 'create'|'edit'|'delete', category? }
  const [expandedCategoryId, setExpandedCategoryId] = useState(null)
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
  const selectedCategoryId = expandedCategoryId != null && tree.some((category) => category.id === expandedCategoryId)
    ? expandedCategoryId
    : null

  const visibleTree = useMemo(() => {
    const ids = new Set(tree.map((category) => category.id))
    const roots = tree.filter((category) => category.parentId == null || !ids.has(category.parentId))
    if (selectedCategoryId == null) return roots

    const byId = new Map(tree.map((category) => [category.id, category]))
    const visibleIds = new Set(roots.map((category) => category.id))
    let current = byId.get(selectedCategoryId)
    while (current) {
      visibleIds.add(current.id)
      current = current.parentId != null ? byId.get(current.parentId) : null
    }

    const descendants = new Set([selectedCategoryId])
    let changed = true
    while (changed) {
      changed = false
      tree.forEach((category) => {
        if (category.parentId != null && descendants.has(category.parentId) && !descendants.has(category.id)) {
          descendants.add(category.id)
          changed = true
        }
      })
    }
    descendants.forEach((id) => visibleIds.add(id))
    return tree.filter((category) => visibleIds.has(category.id))
  }, [tree, selectedCategoryId])

  const categoryColumns = useMemo(() => [
    {
      key: 'name',
      label: 'Name',
      flex: 1.45,
      minWidth: 260,
      render: (category) => {
        const hasChildren = categories.some((item) => item.parentId === category.id)
        return (
          <span className="admin-tree-name" style={{ paddingLeft: `${category.depth * 1.25}rem` }}>
            <span
              className={`admin-tree-caret${selectedCategoryId === category.id ? ' is-expanded' : ''}${hasChildren ? '' : ' is-empty'}`}
              aria-hidden="true"
            >
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" strokeWidth="2">
                <path d="M9 6l6 6-6 6" />
              </svg>
            </span>
            {category.depth > 0 && <span className="admin-tree-branch" aria-hidden="true">└</span>}
            <PoiIconBadge
              {...categoryBadgeAppearance(categories, category.id)}
              size={20}
              label={`${category.name} effective marker${category.color == null || category.iconKey == null ? ' (partly inherited)' : ''}`}
            />
            {category.name}
          </span>
        )
      },
    },
    {
      key: 'poiCount',
      label: 'POIs',
      sortType: 'number',
      sortValue: (category) => category.poiCount,
      flex: 0.55,
      minWidth: 90,
      render: (category) => category.poiCount,
    },
    {
      key: 'actions',
      label: 'Actions',
      fixedWidth: 170,
      sortable: false,
      resizable: false,
      align: 'right',
      render: (category) => (
        <div className="admin-table-actions">
          <button type="button" className="admin-btn admin-btn-sm" onClick={() => setModal({ type: 'edit', category })}>
            Edit
          </button>
          <button type="button" className="admin-btn admin-btn-sm admin-btn-danger" onClick={() => setModal({ type: 'delete', category })}>
            Delete
          </button>
        </div>
      ),
    },
  ], [categories, selectedCategoryId])

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
            Select a category to inspect its related subcategories and POI count.
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
          <AdminTable
            columns={categoryColumns}
            rows={visibleTree}
            getRowKey={(category) => category.id}
            sortRows={sortCategoryTree}
            onRowClick={(category) => setExpandedCategoryId((current) => current === category.id ? null : category.id)}
            selectedRowKey={selectedCategoryId}
          />
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
