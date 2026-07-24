import assert from 'node:assert/strict'
import test from 'node:test'
import { DEMO_DRAWING_THEMES, DEMO_THEME_NOTE } from './demoThemes.js'

test('demo drawing legend preserves the five approved themes and colors', () => {
  assert.deepEqual(DEMO_DRAWING_THEMES, [
    { label: 'Mobility & logistics', color: '#2563EB' },
    { label: 'Emergency & resilience', color: '#DC2626' },
    { label: 'Tourism & heritage', color: '#7C3AED' },
    { label: 'Environment & recreation', color: '#0F766E' },
    { label: 'Municipal services', color: '#EA580C' },
  ])
  assert.equal(
    DEMO_THEME_NOTE,
    'The operational scenario is illustrative; the underlying place and geometry are real.',
  )
})
