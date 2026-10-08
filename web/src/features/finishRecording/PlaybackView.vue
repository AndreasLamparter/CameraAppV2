<script setup lang="ts">
import { computed, onBeforeUnmount, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import AppPage from '@/shell/AppPage.vue'
import { mediaUrls, type Schemas } from '@/api/client'
import { useErrorMessage } from '@/shared/useErrorMessage'
import { formatClock, formatDateTime, formatDifferenceMs } from '@/shared/format'
import { useRecordingsStore, type RecordingMetadata } from './recordingsStore'
import { frameAtTime, nearestIndex, timeOfFrame } from './playbackSync'
import FinishImageViewer from './FinishImageViewer.vue'

const props = defineProps<{ id: string }>()

const { t, te, locale } = useI18n()
const recordings = useRecordingsStore()
const { messageOf } = useErrorMessage()

const ZOOM_STEPS = [0.125, 0.25, 0.5, 1, 2, 4, 8]
const MIN_ZOOM = 0.125

const recording = ref<RecordingMetadata | null>(null)
const loadError = ref<string | null>(null)
const column = ref(0)
const frame = ref(0)
const zoomX = ref(1)
const zoomY = ref(0.5)
const showFinishVideo = ref(false)
const video = ref<HTMLVideoElement | null>(null)
let frameCallback: number | undefined

const front = computed(() => (recording.value?.frontVideo?.available ? recording.value.frontVideo : null))
const columnTime = computed(() => recording.value?.columnTimestamps[column.value] ?? null)
const frameTime = computed(() => front.value?.frameTimestamps[frame.value] ?? null)

function file(kind: Schemas['RecordingFile'], download = false): string {
  return mediaUrls.recordingFile(props.id, kind, download)
}

function errorText(code: string | null | undefined): string | null {
  if (!code) {
    return null
  }
  return te(`errors.${code}`) ? t(`errors.${code}`) : code
}

const videoProblems = computed(() => {
  const r = recording.value
  if (!r) {
    return []
  }
  const problems: string[] = []
  if (r.finishVideo.errorCode) {
    problems.push(`${t('finish.playback.finishVideo')}: ${errorText(r.finishVideo.errorCode)}`)
  }
  if (r.frontVideo?.errorCode) {
    problems.push(`${t('finish.playback.frontVideo')}: ${errorText(r.frontVideo.errorCode)}`)
  }
  return problems
})

watch(
  () => props.id,
  async (id) => {
    recording.value = null
    loadError.value = null
    try {
      const r = await recordings.get(id)
      recording.value = r
      zoomY.value = ZOOM_STEPS.filter((z) => (r.imageHeight - r.timelineHeight) * z <= 420).at(-1) ?? MIN_ZOOM
      // Short recordings are enlarged to a useful width; long ones start unscaled and scroll.
      zoomX.value = ZOOM_STEPS.filter((z) => z >= 1 && r.imageWidth * z <= 1200).at(-1) ?? 1
      // Start at the beginning of the finish event (after the pre-roll).
      selectColumn(Math.max(0, nearestIndex(r.columnTimestamps, Date.parse(r.eventStartedAt) * 1000)))
    } catch (e) {
      loadError.value = messageOf(e)
    }
  },
  { immediate: true },
)

/** Column chosen (click, keyboard): the front video shows the frame closest in time. */
function selectColumn(index: number): void {
  const r = recording.value
  if (!r) {
    return
  }
  column.value = Math.min(Math.max(index, 0), r.columnTimestamps.length - 1)
  const f = front.value
  if (f && columnTime.value !== null) {
    showFrame(nearestIndex(f.frameTimestamps, columnTime.value))
  }
}

/** Front frame chosen (keyboard): the marker moves to the column closest in time. */
function selectFrame(index: number): void {
  const f = front.value
  const r = recording.value
  if (!f || !r) {
    return
  }
  showFrame(index)
  if (frameTime.value !== null) {
    column.value = nearestIndex(r.columnTimestamps, frameTime.value)
  }
}

function showFrame(index: number): void {
  const f = front.value
  if (!f) {
    return
  }
  frame.value = Math.min(Math.max(index, 0), f.frameTimestamps.length - 1)
  const element = video.value
  if (element) {
    element.pause()
    element.currentTime = timeOfFrame(frame.value, f.frameRate)
  }
}

/** While the front video plays, the marker follows it (FS1-62). */
function followVideo(): void {
  const element = video.value
  const f = front.value
  const r = recording.value
  if (!element || !f || !r) {
    return
  }
  frame.value = frameAtTime(element.currentTime, f.frameRate, f.frameTimestamps.length)
  if (frameTime.value !== null) {
    column.value = nearestIndex(r.columnTimestamps, frameTime.value)
  }
}

function onPlay(): void {
  const element = video.value
  if (!element) {
    return
  }
  if ('requestVideoFrameCallback' in element) {
    const tick = () => {
      followVideo()
      if (!element.paused) {
        frameCallback = element.requestVideoFrameCallback(tick)
      }
    }
    frameCallback = element.requestVideoFrameCallback(tick)
  }
}

function onTimeUpdate(): void {
  if (video.value && !video.value.paused) {
    followVideo()
  }
}

function onKeydown(event: KeyboardEvent): void {
  if (event.key !== 'ArrowLeft' && event.key !== 'ArrowRight') {
    return
  }
  event.preventDefault()
  const step = event.key === 'ArrowRight' ? 1 : -1
  if (event.shiftKey) {
    selectFrame(frame.value + step)
  } else {
    // A step to the right in a reversed image is a step back in time.
    selectColumn(column.value + (recording.value?.reverseTimeDirection ? -step : step))
  }
}

function zoom(axis: 'x' | 'y', direction: 1 | -1): void {
  const current = axis === 'x' ? zoomX : zoomY
  const index = ZOOM_STEPS.indexOf(current.value)
  current.value = ZOOM_STEPS[Math.min(Math.max(index + direction, 0), ZOOM_STEPS.length - 1)] ?? current.value
}

onBeforeUnmount(() => {
  if (frameCallback !== undefined && video.value && 'cancelVideoFrameCallback' in video.value) {
    video.value.cancelVideoFrameCallback(frameCallback)
  }
})

const title = computed(() =>
  recording.value
    ? t('finish.playback.title', { time: formatDateTime(recording.value.startedAt, locale.value) })
    : t('finish.recordings.title'),
)
</script>

<template>
  <AppPage id="playback" :title="title">
    <template #leading>
      <UButton icon="i-lucide-arrow-left" color="neutral" variant="ghost" :aria-label="t('finish.playback.back')" :to="{ name: 'recordings' }" />
    </template>
    <template #actions>
      <UDropdownMenu
        v-if="recording"
        :items="[
          [
            { label: t('finish.playback.finishImage'), icon: 'i-lucide-image', href: file('FinishImage', true) },
            ...(recording.finishVideo.available
              ? [{ label: t('finish.playback.finishVideo'), icon: 'i-lucide-film', href: file('FinishVideo', true) }]
              : []),
            ...(front ? [{ label: t('finish.playback.frontVideo'), icon: 'i-lucide-video', href: file('FrontVideo', true) }] : []),
          ],
        ]"
      >
        <UButton icon="i-lucide-download" color="neutral" variant="outline" :label="t('finish.playback.download')" />
      </UDropdownMenu>
    </template>

    <UAlert v-if="loadError" color="error" variant="subtle" :title="loadError" role="alert" />

    <!-- Keyboard: arrows move by one column, Shift + arrows by one front frame. -->
    <div v-if="recording" class="flex flex-col gap-4 outline-none" tabindex="0" data-testid="playback" @keydown="onKeydown">
      <UAlert v-for="(p, i) in videoProblems" :key="i" color="warning" variant="subtle" icon="i-lucide-triangle-alert" :title="p" />

      <div class="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(0,1fr)]">
        <div>
          <h2 class="mb-1 text-sm font-medium text-muted">{{ t('finish.playback.frontVideo') }}</h2>
          <video
            v-if="front"
            ref="video"
            :src="file('FrontVideo')"
            class="w-full rounded-md bg-black"
            controls
            muted
            preload="auto"
            data-testid="front-video"
            @loadedmetadata="showFrame(frame)"
            @play="onPlay"
            @timeupdate="onTimeUpdate"
          />
          <p v-else class="text-sm text-muted">{{ t('finish.playback.noFrontVideo') }}</p>
        </div>
        <dl class="grid h-fit grid-cols-[auto_1fr] gap-x-4 gap-y-1 rounded-md border border-default p-3 text-sm">
          <dt class="text-muted">{{ t('finish.playback.column') }}</dt>
          <dd class="mono" data-testid="column-time">{{ columnTime === null ? '–' : formatClock(columnTime) }}</dd>
          <dt class="text-muted">{{ t('finish.playback.frontFrame') }}</dt>
          <dd class="mono" data-testid="frame-time">{{ frameTime === null ? '–' : formatClock(frameTime) }}</dd>
          <dt class="text-muted">{{ t('finish.playback.difference') }}</dt>
          <dd class="mono" data-testid="time-difference">
            {{ columnTime === null || frameTime === null ? '–' : formatDifferenceMs(columnTime, frameTime) }}
          </dd>
          <dd class="col-span-2 mt-2 text-xs text-dimmed">{{ t('finish.playback.keyboardHint') }}</dd>
        </dl>
      </div>

      <div class="flex flex-wrap items-center gap-2">
        <span class="text-sm text-muted">{{ t('finish.playback.zoomX') }}</span>
        <UButton icon="i-lucide-zoom-out" size="xs" color="neutral" variant="outline" :aria-label="`${t('finish.playback.zoomX')} −`" @click="zoom('x', -1)" />
        <span class="mono w-12 text-center text-sm">{{ zoomX }}×</span>
        <UButton icon="i-lucide-zoom-in" size="xs" color="neutral" variant="outline" :aria-label="`${t('finish.playback.zoomX')} +`" @click="zoom('x', 1)" />
        <span class="ml-4 text-sm text-muted">{{ t('finish.playback.zoomY') }}</span>
        <UButton icon="i-lucide-zoom-out" size="xs" color="neutral" variant="outline" :aria-label="`${t('finish.playback.zoomY')} −`" @click="zoom('y', -1)" />
        <span class="mono w-12 text-center text-sm">{{ zoomY }}×</span>
        <UButton icon="i-lucide-zoom-in" size="xs" color="neutral" variant="outline" :aria-label="`${t('finish.playback.zoomY')} +`" @click="zoom('y', 1)" />
      </div>

      <FinishImageViewer
        :src="file('FinishImage')"
        :width="recording.imageWidth"
        :height="recording.imageHeight"
        :timeline-height="recording.timelineHeight"
        :reverse="recording.reverseTimeDirection"
        :selected-column="column"
        :zoom-x="zoomX"
        :zoom-y="zoomY"
        @select="selectColumn"
      />

      <div v-if="recording.finishVideo.available">
        <UButton
          icon="i-lucide-play"
          color="neutral"
          variant="outline"
          :label="showFinishVideo ? t('finish.playback.hideFinishVideo') : t('finish.playback.showFinishVideo')"
          @click="showFinishVideo = !showFinishVideo"
        />
        <video v-if="showFinishVideo" :src="file('FinishVideo')" class="mt-2 max-h-[50vh] rounded-md bg-black" controls autoplay muted />
      </div>
    </div>
  </AppPage>
</template>
