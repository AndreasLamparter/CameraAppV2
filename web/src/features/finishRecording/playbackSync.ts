/**
 * Synchronization between finish image columns and front video frames (FS1-62). All timestamps are Unix
 * microseconds on the shared time base; both arrays are sorted ascending.
 */

/** Index of the timestamp closest to `value` (the earlier one on a tie); -1 for an empty list. */
export function nearestIndex(timestamps: readonly number[], value: number): number {
  if (timestamps.length === 0) {
    return -1
  }
  let low = 0
  let high = timestamps.length - 1
  while (low < high) {
    const mid = (low + high) >> 1
    if ((timestamps[mid] ?? 0) < value) {
      low = mid + 1
    } else {
      high = mid
    }
  }
  if (low > 0 && value - (timestamps[low - 1] ?? 0) <= (timestamps[low] ?? 0) - value) {
    return low - 1
  }
  return low
}

/** Pixel column of a finish image column; with reversed time direction the earliest column is on the right. */
export function columnToX(column: number, columnCount: number, reverse: boolean): number {
  return reverse ? columnCount - 1 - column : column
}

/** Finish image column under an image x coordinate (image pixels, not screen pixels). */
export function xToColumn(x: number, columnCount: number, reverse: boolean): number {
  const pixel = Math.min(Math.max(Math.floor(x), 0), columnCount - 1)
  return reverse ? columnCount - 1 - pixel : pixel
}

/** Front frame shown at a video time: frame i is displayed from i / frameRate to (i + 1) / frameRate. */
export function frameAtTime(seconds: number, frameRate: number, frameCount: number): number {
  const index = Math.floor(seconds * frameRate + 1e-6)
  return Math.min(Math.max(index, 0), frameCount - 1)
}

/** Video time that shows frame `index` (its middle, robust against rounding in the browser). */
export function timeOfFrame(index: number, frameRate: number): number {
  return (index + 0.5) / frameRate
}
