import { describe, expect, it } from 'vitest'
import { ALL_RACES, filterByRace, hasRecordingsWithoutRace, NO_RACE, raceNames } from './raceFilter'

const recordings = [{ id: 1, raceName: 'Lauf 2' }, { id: 2, raceName: 'Lauf 1' }, { id: 3, raceName: null }, { id: 4, raceName: 'Lauf 2' }]

describe('raceFilter', () => {
  it('lists each race once, sorted', () => {
    expect(raceNames(recordings)).toEqual(['Lauf 1', 'Lauf 2'])
    expect(hasRecordingsWithoutRace(recordings)).toBe(true)
  })

  it('filters by race, by recordings without race, or not at all', () => {
    expect(filterByRace(recordings, 'Lauf 2').map((r) => r.id)).toEqual([1, 4])
    expect(filterByRace(recordings, NO_RACE).map((r) => r.id)).toEqual([3])
    expect(filterByRace(recordings, ALL_RACES)).toHaveLength(4)
  })
})
