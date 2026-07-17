import { useEffect, useMemo, useRef, useState } from 'react'
import { createPoiCategory, listPoiIcons, updatePoiCategory } from '../../api/poi'
import {
  DEFAULT_POI_COLOR,
  POI_ICON_CATALOG,
  categoryBadgeAppearance,
  flattenCategoryTree,
  collectDescendantIds,
  categoryOptionLabel,
} from '../../utils/poiCategories'
import PoiIconBadge from '../../components/PoiIconBadge'

// Create or edit a POI category: a name plus an optional parent, marker color and marker icon.
// Null appearance values inherit the nearest ancestor. The color input cannot express null, so the
// Clear button restores inheritance while still previewing DEFAULT_POI_COLOR when no ancestor sets it.
// In edit mode the category itself and its whole subtree are excluded from the parent options —
// re-parenting into your own descendants would turn the tree into a cycle (server enforces too).
export default function PoiCategoryFormModal({ mode, category, categories, onClose, onSuccess }) {
  const isEdit = mode === 'edit'
  const [name, setName] = useState(category?.name ?? '')
  const [parentId, setParentId] = useState(category?.parentId ?? null)
  const [color, setColor] = useState(category?.color ?? null)
  const [iconKey, setIconKey] = useState(category?.iconKey ?? null)
  const [icons, setIcons] = useState(POI_ICON_CATALOG)
  const [iconsFromApi, setIconsFromApi] = useState(true)
  const [submitting, setSubmitting] = useState(false)
  const [error, setError] = useState('')
  const firstRef = useRef(null)

  useEffect(() => {
    firstRef.current?.focus()
  }, [])

  useEffect(() => {
    let cancelled = false
    listPoiIcons()
      .then((items) => {
        if (!cancelled && Array.isArray(items) && items.length > 0) {
          setIcons(items)
          setIconsFromApi(true)
        }
      })
      .catch(() => {
        // The static allowlist exactly mirrors the endpoint, so category editing remains available
        // during a transient catalogue-read failure without ever offering an arbitrary icon key.
        if (!cancelled) setIconsFromApi(false)
      })
    return () => {
      cancelled = true
    }
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
  const parentAppearance = categoryBadgeAppearance(categories, parentId)
  const previewColor = color ?? parentAppearance.color
  const previewIconKey = iconKey ?? parentAppearance.iconKey

  const handleSubmit = async (event) => {
    event.preventDefault()
    if (!canSubmit) return
    setSubmitting(true)
    setError('')
    try {
      const body = { name: trimmed, parentId, color, iconKey }
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

          <label className="admin-field">
            <span>Marker icon</span>
            <select
              value={iconKey ?? ''}
              onChange={(event) => setIconKey(event.target.value === '' ? null : event.target.value)}
            >
              <option value="">Inherit from parent</option>
              {icons.map((icon) => (
                <option key={icon.key} value={icon.key}>
                  {icon.label}
                </option>
              ))}
            </select>
            {!iconsFromApi && (
              <span className="admin-color-hint">
                Using the built-in icon catalogue while the server catalogue is unavailable.
              </span>
            )}
          </label>

          <div className="admin-marker-preview" aria-label="Effective marker preview">
            <PoiIconBadge
              iconKey={previewIconKey}
              color={previewColor}
              size={32}
              label={`${trimmed || 'Category'} marker preview`}
            />
            <span>
              <strong>Effective marker</strong>
              <small>
                {color === null ? 'Inherited color' : color}
                {' · '}
                {iconKey === null ? 'Inherited icon' : icons.find((icon) => icon.key === iconKey)?.label ?? iconKey}
              </small>
            </span>
          </div>

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
