import {
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from '@microsoft/signalr'
import client, { getStoredAuth } from './client.js'

export const VEHICLE_POSITION_UPDATED = 'VehiclePositionUpdated'

function defaultConnectionFactory() {
  return new HubConnectionBuilder()
    .withUrl(`${client.defaults.baseURL}/hubs/transportation`, {
      accessTokenFactory: () => getStoredAuth()?.token ?? '',
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(LogLevel.Warning)
    .build()
}

// Owns exactly one SignalR connection and one event subscription. Dependency injection of the
// connection factory keeps reconnect/join/cleanup behavior testable without a browser or server.
export function createRouteSimulationConnection({
  onState,
  onMembershipChange = () => {},
  onConnectionState = () => {},
  connectionFactory = defaultConnectionFactory,
  schedule = (callback, delay) => globalThis.setTimeout(callback, delay),
  cancelSchedule = (timer) => globalThis.clearTimeout(timer),
}) {
  const connection = connectionFactory()
  const followed = new Set()
  let disposed = false
  let startPromise = null
  let retryTimer = null

  const notifyMembership = () => onMembershipChange(new Set(followed))

  const receiveState = (state) => {
    if (state?.status === 'NotStarted' && followed.delete(state.routeId)) {
      notifyMembership()
      if (connection.state === HubConnectionState.Connected) {
        connection.invoke('LeaveRoute', state.routeId)
          .then(() => followed.size === 0 ? connection.stop() : undefined)
          .catch(() => {})
      }
    }
    onState(state)
  }

  connection.on(VEHICLE_POSITION_UPDATED, receiveState)
  connection.onreconnecting(() => onConnectionState('reconnecting'))
  connection.onreconnected(async () => {
    onConnectionState('connected')
    await Promise.allSettled([...followed].map((routeId) => connection.invoke('JoinRoute', routeId)))
  })

  const scheduleRetry = () => {
    if (disposed || followed.size === 0 || retryTimer != null) return
    retryTimer = schedule(() => {
      retryTimer = null
      if (followed.size === 0) return
      ensureStarted().catch(() => scheduleRetry())
    }, 3000)
  }

  connection.onclose(() => {
    onConnectionState('disconnected')
    startPromise = null
    scheduleRetry()
  })

  async function ensureStarted() {
    if (disposed) throw new Error('SignalR connection is disposed.')
    if (connection.state === HubConnectionState.Connected) return
    if (startPromise) return startPromise
    onConnectionState('connecting')
    startPromise = connection.start()
      .then(async () => {
        onConnectionState('connected')
        await Promise.all([...followed].map((routeId) => connection.invoke('JoinRoute', routeId)))
      })
      .catch((error) => {
        onConnectionState('disconnected')
        startPromise = null
        scheduleRetry()
        throw error
      })
    return startPromise
  }

  return {
    async follow(routeId) {
      if (followed.has(routeId)) return
      followed.add(routeId)
      notifyMembership()
      try {
        if (connection.state === HubConnectionState.Connected) {
          await connection.invoke('JoinRoute', routeId)
        } else if (connection.state === HubConnectionState.Disconnected) {
          // The start completion joins the complete desired set once.
          await ensureStarted()
        }
      } catch (error) {
        // A failed initial start remains desired and the scheduled retry will join it. A connected
        // hub invocation failure is route-specific, so roll that membership back immediately.
        if (connection.state === HubConnectionState.Connected) {
          followed.delete(routeId)
          notifyMembership()
        }
        throw error
      }
    },

    async unfollow(routeId) {
      if (!followed.delete(routeId)) return
      notifyMembership()
      if (connection.state === HubConnectionState.Connected) {
        await connection.invoke('LeaveRoute', routeId)
      }
      if (followed.size === 0 && connection.state !== HubConnectionState.Disconnected) {
        await connection.stop()
      }
    },

    followedRouteIds() {
      return new Set(followed)
    },

    async dispose() {
      disposed = true
      followed.clear()
      if (retryTimer != null) cancelSchedule(retryTimer)
      retryTimer = null
      connection.off(VEHICLE_POSITION_UPDATED, receiveState)
      if (connection.state !== HubConnectionState.Disconnected) await connection.stop()
    },
  }
}
