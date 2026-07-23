import test from 'node:test'
import assert from 'node:assert/strict'
import { HubConnectionState } from '@microsoft/signalr'
import {
  createRouteSimulationConnection,
  VEHICLE_POSITION_UPDATED,
} from './transportationSignalR.js'

class FakeConnection {
  state = HubConnectionState.Disconnected
  handlers = new Map()
  invocations = []
  starts = 0
  stops = 0
  startFailures = 0

  on(name, handler) { this.handlers.set(name, handler) }
  off(name, handler) {
    if (this.handlers.get(name) === handler) this.handlers.delete(name)
  }
  onreconnecting(handler) { this.reconnecting = handler }
  onreconnected(handler) { this.reconnected = handler }
  onclose(handler) { this.closed = handler }
  async start() {
    this.starts += 1
    if (this.startFailures > 0) {
      this.startFailures -= 1
      throw new Error('offline')
    }
    this.state = HubConnectionState.Connected
  }
  async stop() {
    this.stops += 1
    this.state = HubConnectionState.Disconnected
    this.closed?.()
  }
  async invoke(method, routeId) { this.invocations.push([method, routeId]) }
  emit(name, payload) { this.handlers.get(name)?.(payload) }
}

test('one connection follows multiple routes, rejoins, leaves, and cleans up once', async () => {
  const connection = new FakeConnection()
  const states = []
  const memberships = []
  const manager = createRouteSimulationConnection({
    onState: (state) => states.push(state),
    onMembershipChange: (ids) => memberships.push([...ids]),
    connectionFactory: () => connection,
  })

  await manager.follow(3)
  await manager.follow(4)
  assert.equal(connection.starts, 1)
  assert.deepEqual(connection.invocations, [['JoinRoute', 3], ['JoinRoute', 4]])
  assert.equal(connection.handlers.size, 1)

  connection.emit(VEHICLE_POSITION_UPDATED, { routeId: 3, status: 'Running' })
  assert.equal(states.length, 1)

  connection.state = HubConnectionState.Connected
  await connection.reconnected()
  assert.deepEqual(connection.invocations.slice(-2), [['JoinRoute', 3], ['JoinRoute', 4]])

  await manager.unfollow(3)
  assert.equal(connection.stops, 0)
  await manager.unfollow(4)
  assert.equal(connection.stops, 1)
  assert.deepEqual(memberships.at(-1), [])

  await manager.dispose()
  assert.equal(connection.handlers.size, 0)
})

test('failed initial connection keeps desired membership and retries once connectivity returns', async () => {
  const connection = new FakeConnection()
  connection.startFailures = 1
  let scheduled = null
  let membership = new Set()
  const manager = createRouteSimulationConnection({
    onState: () => {},
    onMembershipChange: (ids) => { membership = ids },
    connectionFactory: () => connection,
    schedule: (callback) => {
      scheduled = callback
      return 1
    },
    cancelSchedule: () => {},
  })

  await assert.rejects(manager.follow(5), /offline/)
  assert.equal(membership.has(5), true)
  assert.equal(typeof scheduled, 'function')

  scheduled()
  await new Promise((resolve) => globalThis.setTimeout(resolve, 0))
  assert.equal(connection.starts, 2)
  assert.deepEqual(connection.invocations, [['JoinRoute', 5]])
  await manager.dispose()
})

test('NotStarted after API restart drops membership and forwards the state', async () => {
  const connection = new FakeConnection()
  const states = []
  let membership = null
  const manager = createRouteSimulationConnection({
    onState: (state) => states.push(state),
    onMembershipChange: (ids) => { membership = ids },
    connectionFactory: () => connection,
  })
  await manager.follow(7)

  connection.emit(VEHICLE_POSITION_UPDATED, { routeId: 7, status: 'NotStarted' })

  assert.equal(membership.has(7), false)
  assert.equal(states.at(-1).status, 'NotStarted')
  await manager.dispose()
})
