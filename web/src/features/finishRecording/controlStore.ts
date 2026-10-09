import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, unwrap, type Schemas } from '@/api/client'

export type FinishStatus = Schemas['FinishRecordingStatus']
export type OperatingMode = Schemas['OperatingMode']

/** Operating state of the cameras, pushed live; commands of FS1-50 (except shutting down). */
export const useControlStore = defineStore('control', () => {
  const status = ref<FinishStatus | null>(null)
  const busy = ref(false)

  function apply(next: FinishStatus): void {
    status.value = next
  }

  async function load(): Promise<void> {
    apply(await unwrap(api.GET('/api/control/status')))
  }

  /** A command without answer (e.g. backend restarted meanwhile) fails after this time instead of blocking the controls. */
  const COMMAND_TIMEOUT_MS = 30_000

  /** Counts running commands, so a stop sent while another command runs does not release the others' lock early. */
  let running = 0

  async function run(command: (signal: AbortSignal) => Promise<FinishStatus>): Promise<void> {
    running++
    busy.value = true
    try {
      apply(await command(AbortSignal.timeout(COMMAND_TIMEOUT_MS)))
    } finally {
      running--
      busy.value = running > 0
    }
  }

  /** Recording needs the race name (FS2-01); preview does not. */
  const start = (mode: OperatingMode, raceName?: string) =>
    run((signal) => unwrap(api.POST('/api/control/start', { body: { mode, raceName: raceName ?? null }, signal })))
  /** Always possible while the cameras run, also while another command is pending (the backend serializes them). */
  const stop = () => run((signal) => unwrap(api.POST('/api/control/stop', { signal })))
  const setTrigger = (active: boolean) => run((signal) => unwrap(api.PUT('/api/control/trigger', { body: { active }, signal })))
  const relearn = () => run((signal) => unwrap(api.POST('/api/control/background/relearn', { signal })))

  return { status, busy, apply, load, start, stop, setTrigger, relearn }
})
