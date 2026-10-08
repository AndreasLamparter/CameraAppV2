/** Select value for "all races"; select items must not use an empty string. */
export const ALL_RACES = '__all__'

/** Select value for recordings without race (made before races existed). */
export const NO_RACE = '__none__'

interface WithRace {
  raceName?: string | null
}

/** The races of the recordings, sorted, for the filter (FS2-05). */
export function raceNames(recordings: WithRace[]): string[] {
  return [...new Set(recordings.map((r) => r.raceName).filter((name): name is string => !!name))].sort((a, b) => a.localeCompare(b))
}

export function hasRecordingsWithoutRace(recordings: WithRace[]): boolean {
  return recordings.some((r) => !r.raceName)
}

export function filterByRace<T extends WithRace>(recordings: T[], race: string): T[] {
  if (race === ALL_RACES) {
    return recordings
  }
  return recordings.filter((r) => (race === NO_RACE ? !r.raceName : r.raceName === race))
}
