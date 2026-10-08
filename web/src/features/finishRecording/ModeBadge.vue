<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useLive } from '@/api/live'
import { useControlStore, type FinishStatus } from './controlStore'
import { lineColor, modeColor } from './statusColors'

/** Operating state in the sidebar, visible on every page. */
const { t } = useI18n()
const control = useControlStore()
useLive<FinishStatus>('status', control.apply, () => control.load().catch(() => undefined))

const status = computed(() => control.status)
</script>

<template>
  <div v-if="status" class="flex flex-col gap-1 rounded-md border border-default p-2 text-sm">
    <UBadge :color="modeColor(status.mode)" variant="subtle" :label="t(`finish.mode.${status.mode}`)" />
    <UBadge
      v-if="status.mode !== 'Stopped'"
      :color="lineColor(status.line.state)"
      variant="subtle"
      :label="t(`finish.lineState.${status.line.state}`)"
    />
  </div>
</template>
