import { describe, expect, it } from 'vitest'
import type { Schemas } from '@/api/client'
import { bestMode, deviceOptions, modeOptions, selectDevice, selectedKey, selectedModeKey, selectMode } from './deviceSelection'

const labels = {
  byIndex: (index: number) => `USB ${index}`,
  inUse: 'in Benutzung',
  notListed: (name: string) => `${name} (nicht gefunden)`,
}

const modeLabels = {
  mode: (m: { width: number; height: number; frameRate: number; format: string }) => `${m.width}x${m.height} ${m.frameRate} ${m.format}`,
  custom: (m: { width: number; height: number; frameRate: number }) => `${m.width}x${m.height} ${m.frameRate} custom`,
}

function camera(device: Partial<Schemas['CameraSettingsDto']> = {}): Schemas['CameraSettingsDto'] {
  return { deviceIndex: 0, width: 1920, height: 1080, frameRate: 90, exposure: null, offsetMs: 0, ...device }
}

const svproPath = '\\\\?\\usb#b'
const svproModes = [
  { width: 1920, height: 1200, frameRate: 90, format: 'MJPG' },
  { width: 640, height: 480, frameRate: 90, format: 'MJPG' },
]
const svpro = { index: 1, name: 'SVPRO', inUse: false, deviceName: 'SVPRO', devicePath: svproPath, modes: svproModes }
const unnamed = { index: 2, name: 'USB 2', inUse: true, deviceName: null, devicePath: null, modes: [] }
const svproKey = selectedKey(camera({ deviceName: 'SVPRO', devicePath: svproPath }))

describe('deviceSelection', () => {
  it('lists the found devices and marks the configured one as selected', () => {
    const settings = camera({ deviceIndex: 5, deviceName: 'SVPRO', devicePath: svproPath.toUpperCase() })

    const options = deviceOptions(settings, [svpro, unnamed], labels)

    expect(options.map((o) => o.label)).toEqual(['1: SVPRO', '2: USB 2 (in Benutzung)'])
    expect(selectedKey(settings)).toBe(options[0]?.value)
  })

  it('keeps a configured device that the search did not find as an option', () => {
    const options = deviceOptions(camera({ deviceName: 'Front', devicePath: null }), [svpro], labels)

    expect(options[0]?.label).toBe('Front (nicht gefunden)')
  })

  it('shows the configured device without a search', () => {
    expect(deviceOptions(camera({ deviceIndex: 3 }), [], labels).map((o) => o.label)).toEqual(['USB 3'])
  })

  it('stores name and path of a named device, and only the index of an unnamed one', () => {
    const settings = camera({ deviceName: 'Old', devicePath: 'old' })

    selectDevice(settings, svproKey, [svpro, unnamed], 'finish')
    expect(settings).toMatchObject({ deviceIndex: 1, deviceName: 'SVPRO', devicePath: svproPath })

    selectDevice(settings, selectedKey(camera({ deviceIndex: 2 })), [svpro, unnamed], 'finish')
    expect(settings).toMatchObject({ deviceIndex: 2, deviceName: null, devicePath: null })
  })

  it('sets the best mode of the chosen camera: highest rate for the finish camera, largest image for the front camera', () => {
    const modes = [
      { width: 1920, height: 1080, frameRate: 30, format: 'MJPG' },
      { width: 1280, height: 720, frameRate: 60, format: 'MJPG' },
      { width: 640, height: 480, frameRate: 60, format: 'MJPG' },
    ]
    const webcam = { ...svpro, modes }
    const finish = camera({ width: 320, height: 240, frameRate: 10 })
    const front = camera({ width: 320, height: 240, frameRate: 10 })

    selectDevice(finish, svproKey, [webcam], 'finish')
    selectDevice(front, svproKey, [webcam], 'front')

    expect(finish).toMatchObject({ width: 1280, height: 720, frameRate: 60 })
    expect(front).toMatchObject({ width: 1920, height: 1080, frameRate: 30 })
    expect(bestMode([], 'finish')).toBeUndefined()
  })

  it('offers the modes of the camera and keeps own values as an extra option', () => {
    const settings = camera({ width: 1000, height: 500, frameRate: 25 })

    const options = modeOptions(settings, svproModes, modeLabels)
    expect(options.map((o) => o.label)).toEqual(['1000x500 25 custom', '1920x1200 90 MJPG', '640x480 90 MJPG'])

    selectMode(settings, options[2]?.value ?? '', svproModes)
    expect(settings).toMatchObject({ width: 640, height: 480, frameRate: 90 })
    expect(modeOptions(settings, svproModes, modeLabels)).toHaveLength(2)
    expect(selectedModeKey(settings)).toBe(options[2]?.value)
  })
})
