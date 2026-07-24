import { useEffect, useMemo, useRef, useState } from 'react'
import './AttributeModal.css'
import ModalCloseButton from './ModalCloseButton'
import { listPoiCategories } from '../api/poi'
import { flattenCategoryTree, categoryOptionLabel } from '../utils/poiCategories'

// Popup shown after the POI tool places a point. Collects the POI's details — name, one of the
// admin-prepared categories, and working hours — before it is saved. Save passes them up; the X
// discards the placed point. The category list is fetched fresh on open so a category the admin
// just added is immediately pickable without a page reload.
export default function PoiFormModal({ onSave, onCancel }) {
  const [name, setName] = useState('')
  const [categoryId, setCategoryId] = useState('')
  const [openTime, setOpenTime] = useState('09:00')
  const [closeTime, setCloseTime] = useState('18:00')
  const [categories, setCategories] = useState(null) // null = loading, [] = none exist
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

  useEffect(() => {
    let cancelled = false
    listPoiCategories()
      .then((data) => {
        if (!cancelled) setCategories(data)
      })
      .catch(() => {
        if (!cancelled) setCategories([])
      })
    return () => {
      cancelled = true
    }
  }, [])

  const options = useMemo(() => flattenCategoryTree(categories ?? []), [categories])

  const trimmedName = name.trim()
  const canSubmit =
    trimmedName.length > 0 && categoryId !== '' && openTime !== '' && closeTime !== '' && !submitting

  const handleSubmit = async (event) => {
    event.preventDefault()
    if (!canSubmit) return
    setSubmitting(true)
    // Save is async (network); the parent closes the modal on success and on failure, so the
    // spinner state here only needs to prevent double-submits.
    await onSave(trimmedName, Number(categoryId), openTime, closeTime)
  }

  return (
    <div className="attr-modal-overlay" role="dialog" aria-modal="true" aria-label="POI details">
      <form className="attr-modal" onSubmit={handleSubmit}>
        <div className="attr-modal-head">
          <h2 className="attr-modal-title">POI details</h2>
          <ModalCloseButton onClick={onCancel} label="Close POI details" />
        </div>

        <label className="attr-modal-field">
          <span>Name *</span>
          <input
            ref={nameInputRef}
            type="text"
            value={name}
            onChange={(event) => setName(event.target.value)}
            placeholder="e.g. Ankara Kalesi Cafe"
            maxLength={80}
          />
        </label>

        <label className="attr-modal-field">
          <span>Category *</span>
          <select value={categoryId} onChange={(event) => setCategoryId(event.target.value)}>
            <option value="" disabled>
              {categories === null ? 'Loading…' : options.length === 0 ? 'No categories available' : 'Choose a category'}
            </option>
            {options.map((c) => (
              <option key={c.id} value={c.id}>
                {categoryOptionLabel(c)}
              </option>
            ))}
          </select>
        </label>

        {categories !== null && options.length === 0 && (
          <p className="attr-modal-message">No categories yet — an admin must create them first.</p>
        )}

        <div className="attr-modal-row">
          <label className="attr-modal-field">
            <span>Opens *</span>
            <input type="time" value={openTime} onChange={(event) => setOpenTime(event.target.value)} />
          </label>
          <label className="attr-modal-field">
            <span>Closes *</span>
            <input type="time" value={closeTime} onChange={(event) => setCloseTime(event.target.value)} />
          </label>
        </div>

        <div className="attr-modal-actions">
          <button type="submit" className="attr-modal-btn attr-modal-save" disabled={!canSubmit}>
            Save
          </button>
        </div>
      </form>
    </div>
  )
}
