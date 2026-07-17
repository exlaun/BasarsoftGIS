import { useState } from 'react'
import {
  categoryBadgeAppearance,
  flattenCategoryTree,
  categoryOptionLabel,
} from '../utils/poiCategories'
import PoiIconBadge from './PoiIconBadge'
import './LocationAnalysisPanel.css'

// Weight rows the analysis starts from: the mentor's minimum (2 criteria). Category '' = unpicked.
const emptyCriterion = () => ({ categoryId: '', weight: '' })

const MIN_CRITERIA = 2
const MAX_CRITERIA = 5

// The Konum Analizi wizard: step 1 picks the target region (province dropdown OR draw-on-map, both
// end as a polygon in MapPage), step 2 collects 2-5 weighted POI-category criteria whose weights
// must total exactly 100 — Start stays disabled until every rule holds (the server re-validates).
// Purely presentational about the map: region state and every map side-effect live in MapPage; this
// component owns only the criteria form.
export default function LocationAnalysisPanel({
  regionMode,
  onRegionModeChange,
  provinces,
  region,
  onProvincePick,
  onClearRegion,
  categories,
  running,
  analysis,
  onStart,
  onReset,
}) {
  const [criteria, setCriteria] = useState([emptyCriterion(), emptyCriterion()])

  const flatCategories = flattenCategoryTree(categories ?? [])
  const pickedIds = new Set(criteria.map((c) => c.categoryId).filter(Boolean))

  const updateCriterion = (index, patch) =>
    setCriteria((rows) => rows.map((row, i) => (i === index ? { ...row, ...patch } : row)))

  const addCriterion = () =>
    setCriteria((rows) => (rows.length < MAX_CRITERIA ? [...rows, emptyCriterion()] : rows))

  const removeCriterion = (index) =>
    setCriteria((rows) => (rows.length > MIN_CRITERIA ? rows.filter((_, i) => i !== index) : rows))

  // Integer weights only; '' while the field is being edited.
  const parseWeight = (value) => {
    const n = Number(value)
    return Number.isInteger(n) && value !== '' ? n : null
  }

  const total = criteria.reduce((sum, row) => sum + (parseWeight(row.weight) ?? 0), 0)
  const rowsComplete = criteria.every((row) => {
    const weight = parseWeight(row.weight)
    return row.categoryId !== '' && weight !== null && weight >= 1 && weight <= 100
  })
  // The mentor's start conditions, all client-checked for instant feedback: a region, 2-5 complete
  // criteria, and a weight total of exactly 100 — otherwise the analysis must not start.
  const canStart = Boolean(region) && rowsComplete && total === 100 && !running

  const handleStart = () => {
    if (!canStart) return
    onStart(
      criteria.map((row) => ({
        categoryId: Number(row.categoryId),
        weight: parseWeight(row.weight),
      })),
    )
  }

  // While a run is on the map the panel shows its summary instead of the form; "New analysis"
  // returns to the form with the previous inputs intact for quick weight tweaking.
  if (analysis) {
    return (
      <section className="konum-panel" aria-label="Location analysis">
        <h2 className="konum-title">Konum Analizi</h2>
        <p className="konum-summary-region">
          Region: <strong>{analysis.provinceName ?? 'Drawn region'}</strong>
        </p>
        <p className="konum-summary-count">
          {analysis.matchedPoiCount} matching {analysis.matchedPoiCount === 1 ? 'POI' : 'POIs'} in the region
        </p>
        <ul className="konum-summary-criteria">
          {analysis.criteria.map((criterion) => (
            <li key={criterion.categoryId}>
              <span className="konum-summary-category">
                <PoiIconBadge
                  {...categoryBadgeAppearance(categories ?? [], criterion.categoryId)}
                  size={18}
                  label={`${criterion.categoryName} marker`}
                />
                <span>{criterion.categoryName}</span>
              </span>
              <strong>{criterion.weight}</strong>
            </li>
          ))}
        </ul>
        <button type="button" className="konum-btn konum-btn-secondary" onClick={onReset}>
          New analysis
        </button>
      </section>
    )
  }

  return (
    <section className="konum-panel" aria-label="Location analysis">
      <h2 className="konum-title">Konum Analizi</h2>

      <div className="konum-step">
        <span className="konum-step-label">1. Target region</span>
        <div className="konum-mode-toggle" role="group" aria-label="Region source">
          <button
            type="button"
            className={`konum-mode-btn${regionMode === 'province' ? ' active' : ''}`}
            onClick={() => onRegionModeChange('province')}
            aria-pressed={regionMode === 'province'}
          >
            Province
          </button>
          <button
            type="button"
            className={`konum-mode-btn${regionMode === 'draw' ? ' active' : ''}`}
            onClick={() => onRegionModeChange('draw')}
            aria-pressed={regionMode === 'draw'}
          >
            Draw on map
          </button>
        </div>

        {regionMode === 'province' ? (
          <select
            className="konum-select"
            value={region?.provinceId ?? ''}
            onChange={(event) => event.target.value && onProvincePick(Number(event.target.value))}
            disabled={provinces == null}
            aria-label="Province"
          >
            <option value="">
              {provinces == null ? 'Loading provinces…' : 'Select a province…'}
            </option>
            {(provinces ?? []).map((province) => (
              <option key={province.id} value={province.id}>
                {province.name}
              </option>
            ))}
          </select>
        ) : (
          <p className="konum-hint">Draw a polygon on the map; a new drawing replaces the old one.</p>
        )}

        {region && (
          <p className="konum-region-chip">
            <span className="konum-region-name">{region.label}</span>
            <button type="button" className="konum-region-clear" onClick={onClearRegion} title="Clear region">
              ×
            </button>
          </p>
        )}
      </div>

      <div className="konum-step">
        <span className="konum-step-label">
          2. Criteria ({MIN_CRITERIA}-{MAX_CRITERIA}, weights must total 100)
        </span>

        {criteria.map((row, index) => (
          // Position-keyed rows: entries are edited in place and only ever removed from the middle,
          // where re-keying the tail rows is acceptable for this short list.
          <div className="konum-criterion" key={index}>
            <select
              className="konum-select konum-criterion-category"
              value={row.categoryId}
              onChange={(event) => updateCriterion(index, { categoryId: event.target.value })}
              disabled={categories == null}
              aria-label={`Criterion ${index + 1} category`}
            >
              <option value="">{categories == null ? 'Loading…' : 'Category…'}</option>
              {flatCategories
                .filter((category) => String(category.id) === row.categoryId || !pickedIds.has(String(category.id)))
                .map((category) => (
                  <option key={category.id} value={category.id}>
                    {categoryOptionLabel(category)}
                  </option>
                ))}
            </select>
            <input
              className="konum-weight"
              type="number"
              min="1"
              max="100"
              step="1"
              placeholder="0"
              value={row.weight}
              onChange={(event) => updateCriterion(index, { weight: event.target.value })}
              aria-label={`Criterion ${index + 1} weight`}
            />
            <button
              type="button"
              className="konum-row-remove"
              onClick={() => removeCriterion(index)}
              disabled={criteria.length <= MIN_CRITERIA}
              title="Remove criterion"
            >
              ×
            </button>
          </div>
        ))}

        <div className="konum-criteria-footer">
          <button
            type="button"
            className="konum-btn konum-btn-secondary"
            onClick={addCriterion}
            disabled={criteria.length >= MAX_CRITERIA}
          >
            + Add criterion
          </button>
          <span
            className={`konum-total${total === 100 ? ' is-valid' : ''}`}
            role="status"
            aria-label={`Weight total ${total} of 100`}
          >
            Total: {total}/100
          </span>
        </div>
      </div>

      <button
        type="button"
        className="konum-btn konum-btn-start"
        onClick={handleStart}
        disabled={!canStart}
      >
        {running ? 'Starting…' : 'Start analysis'}
      </button>
    </section>
  )
}
