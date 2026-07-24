import test from 'node:test'
import assert from 'node:assert/strict'
import {
  simulationProgressMetrics,
  simulationStopContext,
} from './simulationProgress.js'

const stops = (count) => Array.from({ length: count }, (_, index) => ({
  id: index + 1,
  name: `Stop ${index + 1}`,
  sequenceOrder: index + 1,
}))

const labels = (context) => context.entries.map((entry) => entry.stop?.name || entry.label)

test('five-stop context keeps endpoints and shows Moving between stops', () => {
  const context = simulationStopContext(stops(8), {
    status: 'Running',
    progressPercent: 42,
    currentStopIndex: 4,
  })

  assert.deepEqual(labels(context), ['Stop 1', 'Stop 4', 'Moving', 'Stop 6', 'Stop 8'])
  assert.equal(context.isMoving, true)
  assert.equal(context.nearestStop.name, 'Stop 5')
  assert.equal(context.centerStop, null)
})

test('stopped state shows an explicit vehicle state and completed shows the final stop', () => {
  const stopped = simulationStopContext(stops(5), {
    status: 'Stopped',
    progressPercent: 55,
    currentStopIndex: 2,
  })
  const completed = simulationStopContext(stops(5), {
    status: 'Completed',
    progressPercent: 100,
    currentStopIndex: 4,
  })

  assert.equal(labels(stopped).includes('Stopped'), true)
  assert.equal(stopped.entries.find((entry) => entry.type === 'stopped')?.stop, undefined)
  assert.equal(stopped.isMoving, false)
  assert.equal(completed.centerStop.name, 'Stop 5')
  assert.equal(labels(completed).filter((name) => name === 'Stop 5').length, 1)
})

test('short routes omit unavailable context and never duplicate endpoints', () => {
  const twoStops = simulationStopContext(stops(2), {
    status: 'Running',
    progressPercent: 50,
    currentStopIndex: 0,
  })
  const threeStops = simulationStopContext(stops(3), {
    status: 'Stopped',
    progressPercent: 50,
    currentStopIndex: 1,
  })

  assert.deepEqual(labels(twoStops), ['Stop 1', 'Moving', 'Stop 2'])
  assert.deepEqual(labels(threeStops), ['Stop 1', 'Stopped', 'Stop 3'])
  assert.equal(new Set(labels(threeStops)).size, 3)
})

test('progress metrics clamp percentages and calculate completed/remaining distance', () => {
  assert.deepEqual(
    simulationProgressMetrics({ distanceMeters: 10000 }, { progressPercent: 27.5 }),
    {
      completedPercent: 27.5,
      remainingPercent: 72.5,
      completedDistanceMeters: 2750,
      remainingDistanceMeters: 7250,
    },
  )
  assert.equal(simulationProgressMetrics({}, { progressPercent: 130 }).remainingPercent, 0)
})
