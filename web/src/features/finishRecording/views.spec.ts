import { afterEach, describe, expect, it, vi } from 'vitest'
import { defineComponent, h, onMounted, ref, resolveComponent, type Component } from 'vue'
import { createMemoryHistory, createRouter } from 'vue-router'
import ConfirmHost from '@/shared/ConfirmHost.vue'
import { fakeBackend, flush, mountWithApp } from '@/test/setup'
import RecordingsView from './RecordingsView.vue'
import PlaybackView from './PlaybackView.vue'
import SettingsView from './SettingsView.vue'

// Push channel boundary: no SignalR in unit tests; data is loaded once on mount.
vi.mock('@/api/live', () => ({
  useLive: (_topic: string, _onMessage: unknown, reload: () => unknown) => {
    onMounted(() => void reload())
    return { connected: ref(true) }
  },
  stopLive: () => Promise.resolve(),
}))

const mounted: { unmount: () => void }[] = []

afterEach(() => {
  mounted.splice(0).forEach((w) => w.unmount())
  vi.unstubAllGlobals()
  document.body.innerHTML = ''
})

/** Pages need the dashboard frame of the shell, the confirmation host and a router. */
function mountPage(page: Component, props: Record<string, unknown> = {}) {
  const router = createRouter({
    history: createMemoryHistory(),
    routes: [
      { path: '/', component: { render: () => null } },
      { path: '/recordings', name: 'recordings', component: { render: () => null } },
      { path: '/recordings/:id', name: 'playback', component: { render: () => null } },
    ],
  })
  const Host = defineComponent({
    setup: () => () =>
      h(resolveComponent('UApp'), null, () => [
        h(resolveComponent('UDashboardGroup'), null, () => [h(page, props)]),
        h(ConfirmHost),
      ]),
  })
  const wrapper = mountWithApp(Host, { global: { plugins: [router] } })
  mounted.push(wrapper)
  return wrapper
}

const recording = {
  id: '20261007-095959-500',
  startedAt: new Date(2026, 9, 7, 9, 59, 59, 500).toISOString(),
  durationSeconds: 3.5,
  lineRate: 90,
  hasFrontVideo: true,
  hasVideoError: false,
  sizeBytes: 2_500_000,
}

function button(label: string): HTMLButtonElement | undefined {
  return [...document.body.querySelectorAll('button')].find(
    (b) => b.textContent?.trim() === label || b.getAttribute('aria-label')?.startsWith(label),
  )
}

describe('RecordingsView', () => {
  it('Szenario: Aufnahme löschen – after confirming, the recording is deleted and leaves the list', async () => {
    const backend = fakeBackend({
      'GET /api/recordings': { body: [recording] },
      [`DELETE /api/recordings/${recording.id}`]: { status: 204 },
    })
    mountPage(RecordingsView)
    await flush()
    await flush()
    expect(document.body.textContent).toContain('3.5 s')
    expect(document.body.textContent).toContain('2.4 MB')

    button('Löschen')?.click()
    await flush()
    expect(document.body.textContent).toContain('Aufnahme löschen?')
    const confirm = [...document.body.querySelectorAll('[role="dialog"] button')].find((b) => b.textContent?.trim() === 'Löschen') as HTMLButtonElement
    confirm.click()
    await flush()
    await flush()

    expect(backend.requests.some((r) => r.method === 'DELETE' && r.path === `/api/recordings/${recording.id}`)).toBe(true)
    expect(document.body.querySelector('tbody tr')).toBeNull()
  })

  it('Szenario: Löschen abbrechen – the recording stays in the list', async () => {
    const backend = fakeBackend({ 'GET /api/recordings': { body: [recording] } })
    mountPage(RecordingsView)
    await flush()
    await flush()

    button('Löschen')?.click()
    await flush()
    button('Abbrechen')?.click()
    await flush()

    expect(backend.requests.some((r) => r.method === 'DELETE')).toBe(false)
    expect(document.body.querySelectorAll('tbody tr')).toHaveLength(1)
  })
})

describe('PlaybackView', () => {
  const t0 = new Date(2026, 9, 7, 10, 0, 0, 0).getTime() * 1000
  const metadata = {
    id: recording.id,
    startedAt: new Date(t0 / 1000).toISOString(),
    endedAt: new Date(t0 / 1000 + 1000).toISOString(),
    eventStartedAt: new Date(t0 / 1000).toISOString(),
    eventEndedAt: new Date(t0 / 1000 + 1000).toISOString(),
    endReason: 'PostRoll',
    lineRate: 100,
    reverseTimeDirection: false,
    imageWidth: 100,
    imageHeight: 236,
    timelineHeight: 36,
    // Columns every 10 ms, front frames every 33.3 ms with an offset of 5 ms.
    columnTimestamps: Array.from({ length: 100 }, (_, i) => t0 + i * 10_000),
    finishVideo: { available: false, errorCode: 'video.ffmpegMissing' },
    frontVideo: {
      available: true,
      errorCode: null,
      frameRate: 30,
      frameTimestamps: Array.from({ length: 30 }, (_, i) => t0 + 5_000 + Math.round(i * 33_333.3)),
    },
    finishOffsetMs: 0,
    frontOffsetMs: 0,
  }

  function text(testId: string): string {
    return document.body.querySelector(`[data-testid="${testId}"]`)?.textContent?.trim() ?? ''
  }

  it('Szenario: Synchrone Wiedergabe per Klick – shows the nearest front frame and the time difference', async () => {
    fakeBackend({ [`GET /api/recordings/${recording.id}`]: { body: metadata } })
    mountPage(PlaybackView, { id: recording.id })
    await flush()
    await flush()

    const image = document.body.querySelector('[data-testid="finish-image"]') as HTMLElement
    // 100 columns start at 8× horizontal zoom: column 40 spans x 320–327.
    image.dispatchEvent(new MouseEvent('click', { bubbles: true, clientX: 324 }))
    await flush()

    // Column 40 = 10:00:00.400; nearest front frame: 12 at 10:00:00.405.
    expect(text('column-time')).toBe('10:00:00.400')
    expect(text('frame-time')).toBe('10:00:00.405')
    expect(text('time-difference')).toBe('−5.0 ms')
    expect(document.body.textContent).toContain('ffmpeg nicht gefunden')
  })

  it('moves by one column and by one front frame with the keyboard', async () => {
    fakeBackend({ [`GET /api/recordings/${recording.id}`]: { body: metadata } })
    mountPage(PlaybackView, { id: recording.id })
    await flush()
    await flush()
    const panel = document.body.querySelector('[data-testid="playback"]') as HTMLElement

    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', bubbles: true }))
    await flush()
    expect(text('column-time')).toBe('10:00:00.010')

    panel.dispatchEvent(new KeyboardEvent('keydown', { key: 'ArrowRight', shiftKey: true, bubbles: true }))
    await flush()
    expect(text('frame-time')).toBe('10:00:00.038')
    expect(text('column-time')).toBe('10:00:00.040')
  })
})

const settingsResponse = {
  settings: {
    mediaDirectory: null,
    finishCamera: { deviceIndex: 0, width: 1920, height: 1080, frameRate: 90, exposure: null, offsetMs: 0 },
    frontCamera: { deviceIndex: 1, width: 1280, height: 720, frameRate: 30, exposure: null, offsetMs: 0 },
    frontCameraEnabled: true,
    finishLine: { rotation: 'None', position: 960, width: 1, reverseTimeDirection: false },
    detection: {
      pixelThreshold: 25,
      occupancyThresholdPercent: 2,
      preRollSeconds: 0.5,
      postRollSeconds: 1,
      maxDurationSeconds: 60,
      frontPreRollSeconds: 2,
      frontPostRollSeconds: 2,
    },
  },
  effectiveMediaDirectory: 'C:\\ProgramData\\TimingApp\\media',
  appliesOnNextStart: true,
}

describe('SettingsView', () => {
  it('Szenario: Einstellungen während des Betriebs ändern – hints that changes apply from the next start', async () => {
    const backend = fakeBackend({
      'GET /api/settings': { body: settingsResponse },
      'PUT /api/settings': { body: settingsResponse },
    })
    mountPage(SettingsView)
    await flush()
    await flush()

    button('Speichern')?.click()
    await flush()
    await flush()

    expect(backend.requests.some((r) => r.method === 'PUT' && r.path === '/api/settings')).toBe(true)
    expect(document.body.textContent).toContain('ab dem nächsten Start der Kameras')
  })

  it('shows a waiting state while the device search runs, then the device names', async () => {
    fakeBackend({
      'GET /api/settings': { body: settingsResponse },
      'GET /api/cameras/devices': { body: [{ index: 0, name: 'HD Webcam eMeet C980 Pro', inUse: false }] },
    })
    // Holds the device search open until the test releases it.
    const backendFetch = globalThis.fetch
    let release: () => void = () => {}
    const searchDone = new Promise<void>((resolve) => (release = resolve))
    vi.stubGlobal('fetch', async (input: RequestInfo | URL, init?: RequestInit) => {
      const url = input instanceof Request ? input.url : String(input)
      if (url.includes('/api/cameras/devices')) {
        await searchDone
      }
      return backendFetch(input, init)
    })
    mountPage(SettingsView)
    await flush()
    await flush()

    button('Geräte suchen')?.click()
    await flush()

    expect(document.body.querySelector('[role="status"]')?.textContent).toContain('Kameras werden gesucht')
    expect(button('Geräte suchen')?.disabled).toBe(true)

    release()
    await flush()
    await flush()

    expect(document.body.textContent).not.toContain('Kameras werden gesucht')
    expect(document.body.textContent).toContain('0: HD Webcam eMeet C980 Pro')
  })
})
