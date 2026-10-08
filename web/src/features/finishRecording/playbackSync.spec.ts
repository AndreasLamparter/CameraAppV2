import { describe, expect, it } from 'vitest'
import { columnToX, frameAtTime, nearestIndex, timeOfFrame, xToColumn } from './playbackSync'

describe('nearestIndex', () => {
  const timestamps = [1_000, 2_000, 3_000, 4_000]

  it('finds the closest timestamp', () => {
    expect(nearestIndex(timestamps, 2_400)).toBe(1)
    expect(nearestIndex(timestamps, 2_600)).toBe(2)
    expect(nearestIndex(timestamps, -5)).toBe(0)
    expect(nearestIndex(timestamps, 99_999)).toBe(3)
  })

  it('prefers the earlier one on a tie and handles empty lists', () => {
    expect(nearestIndex(timestamps, 2_500)).toBe(1)
    expect(nearestIndex([], 1)).toBe(-1)
  })
})

describe('finish image columns', () => {
  it('maps columns to x and back, also with reversed time direction', () => {
    expect(columnToX(0, 100, false)).toBe(0)
    expect(columnToX(0, 100, true)).toBe(99)
    expect(xToColumn(99.7, 100, true)).toBe(0)
    expect(xToColumn(-3, 100, false)).toBe(0)
    expect(xToColumn(500, 100, false)).toBe(99)
  })
})

describe('front video frames', () => {
  it('seeking to a frame shows that frame', () => {
    for (const rate of [25, 29.97, 30]) {
      for (const index of [0, 1, 17, 299]) {
        expect(frameAtTime(timeOfFrame(index, rate), rate, 300)).toBe(index)
      }
    }
  })

  it('clamps to the existing frames', () => {
    expect(frameAtTime(100, 30, 10)).toBe(9)
    expect(frameAtTime(0, 30, 10)).toBe(0)
  })
})
