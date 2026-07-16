import { useEffect, useMemo, useRef, useState } from 'react'
import { createPoiCategory, updatePoiCategory } from '../../api/poi'
import { flattenCategoryTree, collectDescendantIds, categoryOptionLabel } from '../../utils/poiCategories'

// Fallback shown in the picker while the category has no color of its own (matches POI_COLOR on
// the map). The real value stays null until the admin actually picks something — an
// <input type="color"> can't express "no color", so null is modeled by the Clear button instead.
const DEFAULT_POI_COLOR = '#e11d48'

// Create or edit a POI category: a name plus an optional parent picked from the existing tree and
// an optional marker color (null = POIs in this category inherit the nearest ancestor's color).
// In edit mode the category itself and its whole subtree are excluded from the parent options —
// re-parenting into your own descendants would turn the tree into a cycle (server enforces too).
export default function PoiCategoryFormModal({ mode, category, categories, onClose, onSuccess }) {
  const isEdit = mode === 'edit'
  const [name, setName] = useState(category?.name ?? '')
  const [parentId, setParentId] = useState(category?.parentId ?? null)
  const [color, setColor] = useState(category?.color ?? null)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const firstRef = useRef(null)

  useEffect(() => {
    firstRef.current?.focus()
  }, [])

  useEffect(() => {
    const onKey = (e) => {
      if (e.key === 'Escape') onClose()
    }
    window.addEventListener('keydown', onKey)
    return () => window.removeEventListener('keydown', onKey)
  }, [onClose])

  const parentOptions = useMemo(() => {
    const flat = flattenCategoryTree(categories)
    if (!isEdit) return flat
    const excluded = collectDescendantIds(categories, category.id)
    return flat.filter((c) => !excluded.has(c.id))
  }, [categories, isEdit, category])

  const trimmed = name.trim()
  const canSubmit = trimmed.length > 0 && !submitting

  const handleSubmit = async (event) => {
    event.preventDefault()
    if (!canSubmit) return
    setSubmitting(true)
    setError('')
    try {
      const body = { name: trimmed, parentId, color }
      if (isEdit) {
        await updatePoiCategory(category.id, body)
        onSuccess('Category updated.')
      } else {
        await createPoiCategory(body)
        onSuccess('Category created.')
      }
    } catch (err) {
      const status = err?.response?.status
      setError(
        status === 409 || status === 400
          ? err?.response?.data?.message ?? 'Could not save the category.'
          : 'Could not save the category.',
      )
      setSubmitting(false)
    }
  }

  return (
    <div className="admin-modal-overlay" role="dialog" aria-modal="true" aria-label={isEdit ? 'Edit category' : 'Add category'}>
      <form className="admin-modal" onSubmit={handleSubmit}>
        <div className="admin-modal-head">
          <h2 className="admin-modal-title">{isEdit ? 'Edit category' : 'Add category'}</h2>
        </div>

        <div className="admin-modal-body">
          <label className="admin-field">
            <span>Name</span>
            <input
              ref={firstRef}
              type="text"
              value={name}
              onChange={(e) => setName(e.target.value)}
              placeholder="e.g. Restoran"
              maxLength={80}
            />
          </label>

          <label className="admin-field">
            <span>Parent category</span>
            <select
              value={parentId ?? ''}
              onChange={(e) => setParentId(e.target.value === '' ? null : Number(e.target.value))}
            >
              <option value="">(top level)</option>
              {parentOptions.map((c) => (
                <option key={c.id} value={c.id}>
                  {categoryOptionLabel(c)}
                </option>
              ))}
            </select>
          </label>

          <label className="admin-field">
            <span>Marker color</span>
            <div className="admin-color-row">
              <input
                type="color"
                value={color ?? DEFAULT_POI_COLOR}
                onChange={(e) => setColor(e.target.value)}
                aria-label="Marker color"
              />
              <button
                type="button"
                className="admin-btn admin-btn-sm"
                onClick={() => setColor(null)}
                disabled={color === null}
              >
                Clear
              </button>
              <span className="admin-color-hint">
                {color === null ? 'Inherited from the parent category' : color}
              </span>
            </div>
          </label>

          {error && <p className="admin-error">{error}</p>}
        </div>

        <div className="admin-modal-foot">
          <button type="button" className="admin-btn" onClick={onClose}>
            Cancel
          </button>
          <button type="submit" className="admin-btn admin-btn-primary" disabled={!canSubmit}>
            {isEdit ? 'Save' : 'Create'}
          </button>
        </div>
      </form>
    </div>
  )
}
