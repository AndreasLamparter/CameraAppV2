import type { Schemas } from '@/api/client'

type CameraSettings = Schemas['CameraSettingsDto']
type CameraDevice = Schemas['CameraDevice']
type CameraMode = Schemas['CameraMode']

/** The finish camera favours the highest frame rate (line rate), the front camera the largest image. */
export type CameraPurpose = 'finish' | 'front'

export interface SelectOption {
  value: string
  label: string
}

export interface DeviceLabels {
  byIndex: (index: number) => string
  inUse: string
  notListed: (name: string) => string
}

export interface ModeLabels {
  mode: (mode: { width: number; height: number; frameRate: number; format: string }) => string
  custom: (mode: { width: number; height: number; frameRate: number }) => string
}

/** Identity of a device: its path, else its driver name, else (backends without names) its index. */
function keyOf(name: string | null | undefined, path: string | null | undefined, index: number): string {
  if (path) {
    return `path:${path.toLowerCase()}`
  }
  return name ? `name:${name}` : `index:${index}`
}

export function selectedKey(camera: CameraSettings): string {
  return keyOf(camera.deviceName, camera.devicePath, camera.deviceIndex)
}

function deviceKey(device: CameraDevice): string {
  return keyOf(device.deviceName, device.devicePath, device.index)
}

/**
 * The devices of the last search, plus the configured device when the search did not list it (not connected or no
 * search yet), so the select always shows the current choice.
 */
export function deviceOptions(camera: CameraSettings, devices: CameraDevice[], labels: DeviceLabels): SelectOption[] {
  const options = devices.map((d) => ({
    value: deviceKey(d),
    label: `${d.index}: ${d.name}${d.inUse ? ` (${labels.inUse})` : ''}`,
  }))
  const current = selectedKey(camera)
  if (!options.some((o) => o.value === current)) {
    const name = camera.deviceName ?? labels.byIndex(camera.deviceIndex)
    options.unshift({ value: current, label: devices.length > 0 ? labels.notListed(name) : name })
  }
  return options
}

/**
 * Applies the device chosen in the select and its best capture mode, when the camera reports modes. A device without
 * driver name is selected by index only.
 */
export function selectDevice(camera: CameraSettings, key: string, devices: CameraDevice[], purpose: CameraPurpose): void {
  const device = devices.find((d) => deviceKey(d) === key)
  if (!device) {
    return
  }
  camera.deviceIndex = device.index
  camera.deviceName = device.deviceName ?? null
  camera.devicePath = device.devicePath ?? null
  const best = bestMode(device.modes, purpose)
  if (best) {
    applyModeValues(camera, best)
  }
}

/** Capture modes of the configured device from the last search; empty when unknown. */
export function modesOf(camera: CameraSettings, devices: CameraDevice[]): CameraMode[] {
  const current = selectedKey(camera)
  return devices.find((d) => deviceKey(d) === current)?.modes ?? []
}

function modeKey(width: number, height: number, frameRate: number): string {
  return `${width}x${height}@${Math.round(frameRate)}`
}

export function selectedModeKey(camera: CameraSettings): string {
  return modeKey(camera.width, camera.height, camera.frameRate)
}

/** The modes of the device, plus the configured values when they match none of them. */
export function modeOptions(camera: CameraSettings, modes: CameraMode[], labels: ModeLabels): SelectOption[] {
  const options = modes.map((m) => ({ value: modeKey(m.width, m.height, m.frameRate), label: labels.mode(m) }))
  const current = selectedModeKey(camera)
  if (!options.some((o) => o.value === current)) {
    options.unshift({ value: current, label: labels.custom(camera) })
  }
  return options
}

export function selectMode(camera: CameraSettings, key: string, modes: CameraMode[]): void {
  const mode = modes.find((m) => modeKey(m.width, m.height, m.frameRate) === key)
  if (mode) {
    applyModeValues(camera, mode)
  }
}

export function bestMode(modes: CameraMode[], purpose: CameraPurpose): CameraMode | undefined {
  const area = (m: CameraMode) => m.width * m.height
  const [primary, secondary] = purpose === 'finish' ? [(m: CameraMode) => m.frameRate, area] : [area, (m: CameraMode) => m.frameRate]
  return [...modes].sort((a, b) => primary(b) - primary(a) || secondary(b) - secondary(a))[0]
}

function applyModeValues(camera: CameraSettings, mode: CameraMode): void {
  camera.width = mode.width
  camera.height = mode.height
  camera.frameRate = Math.round(mode.frameRate)
}
