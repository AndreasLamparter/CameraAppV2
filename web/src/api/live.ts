import { HubConnectionBuilder, HubConnectionState, LogLevel, type HubConnection } from '@microsoft/signalr'
import { onBeforeUnmount, onMounted, ref, type Ref } from 'vue'

/** Push messages of the finish hub: "status" carries the full status, "recordingsChanged" is a reload hint. */
export type LiveTopic = 'status' | 'recordingsChanged'

const connected = ref(false)
let connection: HubConnection | undefined
let starting: Promise<void> | undefined

function ensureConnection(): HubConnection {
  if (connection) {
    return connection
  }
  connection = new HubConnectionBuilder()
    .withUrl('/hubs/finish')
    .withAutomaticReconnect({
      nextRetryDelayInMilliseconds: (ctx) => Math.min(1000 * 2 ** ctx.previousRetryCount, 10_000),
    })
    .configureLogging(LogLevel.Warning)
    .build()
  connection.onreconnecting(() => (connected.value = false))
  connection.onreconnected(() => (connected.value = true))
  connection.onclose(() => {
    connected.value = false
    starting = undefined
    setTimeout(start, 5000)
  })
  return connection
}

function start(): void {
  const c = ensureConnection()
  if (c.state !== HubConnectionState.Disconnected || starting) {
    return
  }
  starting = c
    .start()
    .then(() => {
      connected.value = true
    })
    .catch(() => {
      connected.value = false
      setTimeout(start, 5000)
    })
    .finally(() => {
      starting = undefined
    })
}

/** Stops the push connection (logout). */
export async function stopLive(): Promise<void> {
  const c = connection
  connection = undefined
  connected.value = false
  if (c) {
    c.onclose(() => undefined)
    await c.stop()
  }
}

/**
 * Subscribes to a push topic while the calling component is mounted. Tolerates disconnects: while the push
 * connection is down, `reload` is polled every few seconds instead; it also runs once on mount.
 */
export function useLive<T = void>(
  topic: LiveTopic,
  onMessage: (payload: T) => void,
  reload: () => void | Promise<void>,
  pollMs = 2000,
): { connected: Ref<boolean> } {
  const handler = (payload: T) => onMessage(payload)
  let timer: ReturnType<typeof setInterval> | undefined

  onMounted(() => {
    const c = ensureConnection()
    c.on(topic, handler)
    start()
    timer = setInterval(() => {
      if (!connected.value) {
        void reload()
      }
    }, pollMs)
    void reload()
  })

  onBeforeUnmount(() => {
    connection?.off(topic, handler)
    clearInterval(timer)
  })

  return { connected }
}
