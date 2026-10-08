import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, unwrap, type Schemas } from '@/api/client'

export type RecordingSummary = Schemas['RecordingSummary']
export type RecordingMetadata = Schemas['RecordingMetadata']

/** Complete recordings (FS1-61, FS1-63); reloaded on the "recordingsChanged" push. */
export const useRecordingsStore = defineStore('recordings', () => {
  const items = ref<RecordingSummary[]>([])
  const loaded = ref(false)

  async function load(): Promise<void> {
    items.value = await unwrap(api.GET('/api/recordings'))
    loaded.value = true
  }

  async function get(id: string): Promise<RecordingMetadata> {
    return unwrap(api.GET('/api/recordings/{id}', { params: { path: { id } } }))
  }

  async function remove(id: string): Promise<void> {
    await unwrap(api.DELETE('/api/recordings/{id}', { params: { path: { id } } }))
    items.value = items.value.filter((r) => r.id !== id)
  }

  return { items, loaded, load, get, remove }
})
