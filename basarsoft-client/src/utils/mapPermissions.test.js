import test from 'node:test'
import assert from 'node:assert/strict'
import { canDeletePoi, disabledMapTools, isInsideAuthorizedArea } from './mapPermissions.js'

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

test('the area gate mirrors the server: no boundary means unrestricted', () => {
  // Minimal stand-ins for the OpenLayers geometry API this helper touches.
  const point = (coord) => ({ getType: () => 'Point', getCoordinates: () => coord })
  const line = (coords) => ({ getType: () => 'LineString', getCoordinates: () => coords })
  // The authorized area: accept anything with both ordinates in [0, 1].
  const unitSquare = { intersectsCoordinate: ([x, y]) => x >= 0 && x <= 1 && y >= 0 && y <= 1 }

  assert.equal(isInsideAuthorizedArea(point([0.5, 0.5]), unitSquare), true)
  assert.equal(isInsideAuthorizedArea(point([10, 10]), unitSquare), false)

  // A line counts as inside only when EVERY vertex is inside.
  assert.equal(isInsideAuthorizedArea(line([[0.2, 0.2], [0.8, 0.8]]), unitSquare), true)
  assert.equal(isInsideAuthorizedArea(line([[0.2, 0.2], [10, 10]]), unitSquare), false)

  // Null boundary = no assigned area = unrestricted, matching GetEffectiveAreaAsync returning null.
  assert.equal(isInsideAuthorizedArea(point([10, 10]), null), true)
})
