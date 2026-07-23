import test from 'node:test'
import assert from 'node:assert/strict'
import { createVehicleAnimator } from './vehicleAnimator.js'

test('first Running push snaps, then eases from the previous position to the new target', () => {
  const animator = createVehicleAnimator()

  // First target snaps into place (no prior position to ease from).
  animator.pushTarget(7, { progressPercent: 0, status: 'Running' }, 1000)
  assert.equal(animator.easedProgress(7, 1000), 0)

  // Second target, 1s later: eases 0 -> 40 across ~1000ms.
  animator.pushTarget(7, { progressPercent: 40, status: 'Running' }, 2000)
  assert.equal(animator.easedProgress(7, 2000), 0)
  assert.equal(animator.easedProgress(7, 2500), 20)
  assert.equal(animator.easedProgress(7, 3000), 40)
  // Never overshoots past the target while waiting for the next push.
  assert.equal(animator.easedProgress(7, 5000), 40)
})

test('terminal states snap to their final progress and stop being animated', () => {
  const animator = createVehicleAnimator()
  animator.pushTarget(7, { progressPercent: 0, status: 'Running' }, 0)
  animator.pushTarget(7, { progressPercent: 60, status: 'Running' }, 1000)
  assert.deepEqual(animator.animatingRouteIds(), [7])

  // Completed at 100 snaps immediately regardless of the eased midpoint.
  animator.pushTarget(7, { progressPercent: 100, status: 'Completed' }, 1500)
  assert.equal(animator.easedProgress(7, 1500), 100)
  assert.equal(animator.easedProgress(7, 1501), 100)
  // A snapped/terminal route no longer keeps the animation loop alive.
  assert.deepEqual(animator.animatingRouteIds(), [])
})

test('progress is clamped and routes are tracked independently', () => {
  const animator = createVehicleAnimator()
  animator.pushTarget(7, { progressPercent: -5, status: 'Running' }, 0)
  animator.pushTarget(9, { progressPercent: 130, status: 'Running' }, 0)
  assert.equal(animator.easedProgress(7, 0), 0)
  assert.equal(animator.easedProgress(9, 0), 100)

  assert.equal(animator.has(7), true)
  animator.remove(7)
  assert.equal(animator.has(7), false)
  assert.equal(animator.has(9), true)
})
