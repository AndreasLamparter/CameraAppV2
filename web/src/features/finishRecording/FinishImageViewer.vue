<script setup lang="ts">
import { computed, nextTick, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { columnToX, xToColumn } from './playbackSync'

/**
 * Finish image, zoomable and scrollable in both directions; a click selects a column (FS1-62). The timeline strip
 * at the bottom of the image follows the horizontal zoom only, so it stays readable at any vertical zoom.
 */
const props = defineProps<{
  src: string
  width: number
  height: number
  timelineHeight: number
  reverse: boolean
  selectedColumn: number
  zoomX: number
  zoomY: number
  /** Passages (FS2-15): column and label of each mark. */
  marks?: { column: number; label: string }[]
}>()
const emit = defineEmits<{ select: [column: number] }>()

const { t } = useI18n()
const scroller = ref<HTMLElement | null>(null)

const scaledWidth = computed(() => props.width * props.zoomX)
const linesHeight = computed(() => (props.height - props.timelineHeight) * props.zoomY)

const linesStyle = computed(() => ({
  width: `${scaledWidth.value}px`,
  height: `${linesHeight.value}px`,
  backgroundImage: `url("${props.src}")`,
  backgroundSize: `${scaledWidth.value}px ${props.height * props.zoomY}px`,
}))

const timelineStyle = computed(() => ({
  width: `${scaledWidth.value}px`,
  height: `${props.timelineHeight}px`,
  backgroundImage: `url("${props.src}")`,
  backgroundSize: `${scaledWidth.value}px ${props.height}px`,
  backgroundPosition: `0 -${props.height - props.timelineHeight}px`,
}))

const markerLeft = computed(() => (columnToX(props.selectedColumn, props.width, props.reverse) + 0.5) * props.zoomX)

const markLefts = computed(() =>
  (props.marks ?? []).map((mark) => ({ ...mark, left: (columnToX(mark.column, props.width, props.reverse) + 0.5) * props.zoomX })),
)

function onClick(event: MouseEvent): void {
  const target = event.currentTarget as HTMLElement
  const x = (event.clientX - target.getBoundingClientRect().left) / props.zoomX
  emit('select', xToColumn(x, props.width, props.reverse))
}

/** Keeps the marker visible while it moves (video playback, keyboard). */
watch([markerLeft, () => props.zoomX], async () => {
  await nextTick()
  const element = scroller.value
  if (!element) {
    return
  }
  const left = markerLeft.value
  if (left < element.scrollLeft || left > element.scrollLeft + element.clientWidth) {
    element.scrollLeft = Math.max(0, left - element.clientWidth / 2)
  }
})
</script>

<template>
  <div ref="scroller" class="max-h-[65vh] overflow-auto rounded-md border border-default bg-black">
    <div
      class="relative cursor-crosshair"
      role="img"
      :aria-label="t('finish.playback.finishImage')"
      :style="{ width: `${scaledWidth}px` }"
      data-testid="finish-image"
      @click="onClick"
    >
      <div class="pixelated bg-no-repeat" :style="linesStyle" />
      <div class="pixelated bg-no-repeat" :style="timelineStyle" />
      <div
        v-for="(mark, i) in markLefts"
        :key="i"
        class="pointer-events-none absolute top-0 bottom-0 w-px bg-amber-400"
        :style="{ left: `${mark.left}px` }"
        data-testid="passage-mark"
      >
        <span class="mono absolute top-0 left-1 rounded bg-amber-400 px-1 text-xs text-black">{{ mark.label }}</span>
      </div>
      <div
        class="pointer-events-none absolute top-0 bottom-0 w-px bg-red-500 shadow-[0_0_0_1px_rgba(255,255,255,0.6)]"
        :style="{ left: `${markerLeft}px` }"
        data-testid="column-marker"
      />
    </div>
  </div>
</template>

<style scoped>
.pixelated {
  image-rendering: pixelated;
}
</style>
