<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { mediaUrls, type Schemas } from '@/api/client'
import { lineCssColor } from './statusColors'

/** MJPEG live image of one preview; the stream only runs (and is only encoded) while this element exists. */
const props = defineProps<{
  kind: Schemas['PreviewKind']
  title: string
  active: boolean
  overlay?: Schemas['LineOverlay'] | null
  lineState?: Schemas['LineState']
  /** Full width at the natural aspect ratio (running finish image with its timeline). */
  stretch?: boolean
}>()

const { t } = useI18n()
const src = computed(() => mediaUrls.livePreview(props.kind))

const lineStyle = computed(() => {
  const overlay = props.overlay
  if (!overlay || !props.lineState) {
    return null
  }
  const color = lineCssColor(props.lineState)
  const position = `${overlay.position * 100}%`
  const size = `max(3px, ${overlay.width * 100}%)`
  // The preview shows the turned image, in which the finish line is always vertical.
  return { left: position, width: size, top: 0, bottom: 0, background: color }
})
</script>

<template>
  <figure class="flex flex-col gap-1">
    <figcaption class="text-sm font-medium text-muted">{{ title }}</figcaption>
    <div class="overflow-hidden rounded-md bg-black">
      <!-- The wrapper takes the size of the image, so the overlay percentages also fit a portrait (turned) image. -->
      <div v-if="active" class="relative mx-auto" :class="stretch ? 'w-full' : 'w-fit'">
        <img :src="src" :alt="title" class="block" :class="stretch ? 'w-full' : 'max-h-[70vh] max-w-full'" />
        <div v-if="lineStyle" class="pointer-events-none absolute opacity-80" :style="lineStyle" data-testid="finish-line" />
      </div>
      <div v-else class="grid aspect-video place-items-center text-sm text-dimmed">{{ t('finish.live.noImage') }}</div>
    </div>
  </figure>
</template>
