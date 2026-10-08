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

  async function run(command: () => Promise<FinishStatus>): Promise<void> {
    busy.value = true
    try {
      apply(await command())
    } finally {
      busy.value = false
    }
  }

  const start = (mode: OperatingMode) => run(() => unwrap(api.POST('/api/control/start', { body: { mode } })))
  const stop = () => run(() => unwrap(api.POST('/api/control/stop')))
  const setTrigger = (active: boolean) => run(() => unwrap(api.PUT('/api/control/trigger', { body: { active } })))
  const relearn = () => run(() => unwrap(api.POST('/api/control/background/relearn')))

  return { status, busy, apply, load, start, stop, setTrigger, relearn }
})
