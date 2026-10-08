import { describe, expect, it } from 'vitest'
import { formatBytes, formatClock, formatDifferenceMs } from './format'

describe('formatClock', () => {
  it('Szenario: Uhrzeit wird abgeschnitten – 10:00:00.4567 is shown as 10:00:00.456', () => {
    const localTenOClock = new Date(2026, 9, 7, 10, 0, 0, 456).getTime()
    const micros = localTenOClock * 1000 + 700

    expect(formatClock(micros)).toBe('10:00:00.456')
  })

  it('shows ISO timestamps in local time with milliseconds', () => {
    const iso = new Date(2026, 9, 7, 9, 59, 59, 5).toISOString()

    expect(formatClock(iso)).toBe('09:59:59.005')
  })
})

describe('formatDifferenceMs', () => {
  it('shows a signed difference with one decimal', () => {
    expect(formatDifferenceMs(1_000_012_300, 1_000_000_000)).toBe('+12.3 ms')
    expect(formatDifferenceMs(1_000_000_000, 1_000_040_000)).toBe('−40.0 ms')
    expect(formatDifferenceMs(5, 5)).toBe('±0.0 ms')
  })
})

describe('formatBytes', () => {
  it('uses binary units', () => {
    expect(formatBytes(512)).toBe('512 B')
    expect(formatBytes(1536)).toBe('1.5 KB')
    expect(formatBytes(5 * 1024 * 1024)).toBe('5.0 MB')
  })
})
