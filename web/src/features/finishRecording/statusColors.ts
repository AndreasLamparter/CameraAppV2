import type { Schemas } from '@/api/client'

type Color = 'neutral' | 'info' | 'success' | 'warning' | 'error'

export function modeColor(mode: Schemas['OperatingMode']): Color {
  return mode === 'Recording' ? 'error' : mode === 'Preview' ? 'info' : 'neutral'
}

/** Free = green, occupied = amber, running recording = red (FS1-60). */
export function lineColor(state: Schemas['LineState']): Color {
  return state === 'Recording' ? 'error' : state === 'Occupied' ? 'warning' : 'success'
}

/** CSS color of the finish line overlay, matching {@link lineColor}. */
export function lineCssColor(state: Schemas['LineState']): string {
  return state === 'Recording' ? 'rgb(239 68 68)' : state === 'Occupied' ? 'rgb(245 158 11)' : 'rgb(34 197 94)'
}

export function cameraColor(state: Schemas['CameraState']): Color {
  return state === 'Error' ? 'error' : state === 'Running' ? 'success' : state === 'Starting' ? 'info' : 'neutral'
}
