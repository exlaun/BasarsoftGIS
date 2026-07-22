import test from 'node:test'
import assert from 'node:assert/strict'
import { canDeletePoi, disabledMapTools } from './mapPermissions.js'

test('transportation-only operators are read-only for POIs', () => {
  const config = {
    Poi: { permission: 'add_poi' },
    AddStop: { permission: 'manage_transport' },
  }
  const disabled = disabledMapTools(config, ['manage_transport'])
  assert.equal(disabled.has('Poi'), true)
  assert.equal(disabled.has('AddStop'), false)
  assert.equal(canDeletePoi(['manage_transport']), false)
  assert.equal(canDeletePoi(['manage_pois']), true)
})
