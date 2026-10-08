function pad(value: number, length = 2): string {
  return value.toString().padStart(length, '0')
}

/**
 * Local time as HH:mm:ss.fff (FS1-70). Fractions are truncated, not rounded. Accepts Unix microseconds (recording
 * timestamps) or an ISO string.
 */
export function formatClock(value: number | string): string {
  const ms = typeof value === 'number' ? Math.floor(value / 1000) : Date.parse(value)
  const date = new Date(ms)
  const millis = ((ms % 1000) + 1000) % 1000
  return `${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}.${pad(millis, 3)}`
}

/** Local date and time without fractions, for lists. */
export function formatDateTime(iso: string, locale: string): string {
  return new Date(iso).toLocaleString(locale, {
    year: 'numeric',
    month: '2-digit',
    day: '2-digit',
    hour: '2-digit',
    minute: '2-digit',
    second: '2-digit',
  })
}

/** Signed difference in milliseconds between two Unix-microsecond timestamps, e.g. "+12.3 ms". */
export function formatDifferenceMs(microsA: number, microsB: number): string {
  const ms = (microsA - microsB) / 1000
  const sign = ms > 0 ? '+' : ms < 0 ? '−' : '±'
  return `${sign}${Math.abs(ms).toFixed(1)} ms`
}

export function formatSeconds(seconds: number): string {
  return `${seconds.toFixed(1)} s`
}

export function formatBytes(bytes: number): string {
  if (bytes < 1024) {
    return `${bytes} B`
  }
  const units = ['KB', 'MB', 'GB']
  let value = bytes / 1024
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${value.toFixed(1)} ${units[unit]}`
}
