import test from 'node:test'
import assert from 'node:assert/strict'
import {
  endSimulationErrorMessage,
  formatSimulationProgress,
  reconcileSimulationState,
  simulationControls,
  simulationForRoute,
} from './routeSimulation.js'

test('missing routes expose a stable NotStarted simulation', () => {
  assert.deepEqual(simulationForRoute({}, 9), {
    runId: null,
    routeId: 9,
    status: 'NotStarted',
    progressPercent: 0,
    sequence: 0,
  })
})

test('state reconciliation rejects old sequences and old runs', () => {
  const current = {
    routeId: 1,
    runId: 'new',
    startedAt: '2026-07-23T10:00:00Z',
    sequence: 5,
    progressPercent: 50,
    status: 'Running',
  }
  assert.equal(reconcileSimulationState(current, { ...current, sequence: 4 }), current)
  assert.equal(reconcileSimulationState(current, { ...current, sequence: 6 }).sequence, 6)
  assert.equal(reconcileSimulationState(current, {
    ...current,
    runId: 'old',
    startedAt: '2026-07-23T09:00:00Z',
    sequence: 99,
  }), current)
  assert.equal(reconcileSimulationState(current, {
    routeId: 1,
    status: 'NotStarted',
    runId: null,
  }).status, 'NotStarted')
})

const actionsOf = (controls) => controls.actions.map((a) => a.action)

test('simulation controls expose only the actions valid for the current run state', () => {
  const route = { stopCount: 2, geometryWkt: 'LINESTRING(0 0,1 1)', isGeometryStale: false }

  const start = simulationControls({ simulation: { status: 'NotStarted' }, canControl: true, cameraFollowed: false, route })
  assert.deepEqual(actionsOf(start), ['start'])
  assert.equal(start.actions[0].label, 'Start Simulation')
  assert.equal(start.actions[0].disabled, false)
  assert.equal(start.showFollow, false)
  assert.equal(start.followDisabled, true)

  const running = simulationControls({ simulation: { status: 'Running' }, canControl: true, cameraFollowed: false, route })
  assert.deepEqual(actionsOf(running), ['stop', 'end'])
  assert.equal(running.followLabel, 'Follow')
  assert.equal(running.followDisabled, false)

  const stopped = simulationControls({ simulation: { status: 'Stopped' }, canControl: true, cameraFollowed: true, route })
  assert.deepEqual(actionsOf(stopped), ['resume', 'end'])
  assert.equal(stopped.actions[0].label, 'Resume Simulation')

  const completed = simulationControls({ simulation: { status: 'Completed' }, canControl: true, cameraFollowed: true, route })
  assert.deepEqual(actionsOf(completed), ['end'])
  assert.equal(completed.followLabel, 'Stop Following')
  assert.equal(completed.followDisabled, false)

  const viewer = simulationControls({ simulation: { status: 'Running' }, canControl: false, cameraFollowed: false, route })
  assert.deepEqual(actionsOf(viewer), [])
  assert.equal(viewer.followDisabled, false)
})

test('route readiness gates only a first start, never active-run controls', () => {
  const invalidRoute = { stopCount: 1, geometryWkt: null }

  const notStarted = simulationControls({
    simulation: { status: 'NotStarted' }, canControl: true, cameraFollowed: false, route: invalidRoute,
  })
  assert.equal(notStarted.actions[0].disabled, true)

  const running = simulationControls({
    simulation: { status: 'Running' }, canControl: true, cameraFollowed: false, route: invalidRoute,
  })
  const byAction = Object.fromEntries(running.actions.map((a) => [a.action, a]))
  assert.equal(byAction.stop.disabled, false)
  assert.equal(byAction.end.disabled, false)

  assert.equal(formatSimulationProgress(47.25), '47.3%')
  assert.equal(formatSimulationProgress(120), '100.0%')
})

test('camera follow is available for paused and completed markers, but not a new run', () => {
  const route = { stopCount: 2, geometryWkt: 'LINESTRING(0 0,1 1)', isGeometryStale: false }
  const stopped = simulationControls({
    simulation: { status: 'Stopped' }, canControl: false, cameraFollowed: false, route,
  })
  const completed = simulationControls({
    simulation: { status: 'Completed' }, canControl: false, cameraFollowed: false, route,
  })
  const notStarted = simulationControls({
    simulation: { status: 'NotStarted' }, canControl: false, cameraFollowed: false, route,
  })
  assert.equal(stopped.followDisabled, false)
  assert.equal(completed.followDisabled, false)
  assert.equal(notStarted.followDisabled, true)
})

test('end failures explain stale APIs, authorization, connectivity, and server errors', () => {
  assert.match(
    endSimulationErrorMessage({ response: { status: 404, data: {} } }),
    /restart or redeploy the API/i,
  )
  assert.match(
    endSimulationErrorMessage({ response: { status: 403, data: {} } }),
    /permission/i,
  )
  assert.match(endSimulationErrorMessage(new Error('Network Error')), /could not reach the API/i)
  assert.equal(
    endSimulationErrorMessage({
      response: { status: 500, data: { message: 'Simulation worker failed.' } },
    }),
    'Simulation worker failed.',
  )
})
