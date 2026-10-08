<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import AppPage from '@/shell/AppPage.vue'
import { useLive } from '@/api/live'
import type { Schemas } from '@/api/client'
import { useErrorMessage } from '@/shared/useErrorMessage'
import { formatClock } from '@/shared/format'
import { useControlStore, type FinishStatus, type OperatingMode } from './controlStore'
import { cameraColor, lineColor, modeColor } from './statusColors'
import LivePreview from './LivePreview.vue'
import RaceDialog from './RaceDialog.vue'

const { t, te } = useI18n()
const control = useControlStore()
const { showError, messageOf } = useErrorMessage()
const { connected } = useLive<FinishStatus>('status', control.apply, () => control.load().catch(() => undefined), 1000)

const status = computed(() => control.status)
const running = computed(() => !!status.value && status.value.mode !== 'Stopped')

async function command(action: () => Promise<void>): Promise<void> {
  try {
    await action()
  } catch (e) {
    showError(e)
  }
}

const start = (mode: OperatingMode) => command(() => control.start(mode))
const stop = () => command(() => control.stop())

const LAST_RACE_KEY = 'finish.lastRace'

function lastRace(): string {
  try {
    return localStorage.getItem(LAST_RACE_KEY) ?? ''
  } catch {
    return ''
  }
}

/** Recording starts only with a race name from the dialog (FS2-01); starting again changes the race (FS2-02). */
const raceDialogOpen = ref(false)
const raceError = ref<string | null>(null)
const raceInitial = computed(() => status.value?.raceName ?? lastRace())

function askRace(): void {
  raceError.value = null
  raceDialogOpen.value = true
}

async function startRecording(name: string): Promise<void> {
  raceError.value = null
  try {
    await control.start('Recording', name)
    try {
      localStorage.setItem(LAST_RACE_KEY, name)
    } catch {
      // Remembering the name is a convenience only.
    }
    raceDialogOpen.value = false
  } catch (e) {
    raceError.value = messageOf(e)
  }
}
const toggleTrigger = () => command(() => control.setTrigger(!status.value?.line.manualTrigger))
const relearn = () => command(() => control.relearn())

function errorText(code: string | null | undefined): string | null {
  if (!code) {
    return null
  }
  const key = `errors.${code}`
  return te(key) ? t(key) : code
}

const cameras = computed(() => {
  const s = status.value
  if (!s) {
    return []
  }
  const entry = (key: 'finishCamera' | 'frontCamera', camera: Schemas['CameraStatus']) => ({
    key,
    title: t(`finish.live.${key}`),
    camera,
    error: errorText(camera.errorCode),
    warning: errorText(camera.warningCode),
  })
  return [entry('finishCamera', s.finishCamera), entry('frontCamera', s.frontCamera)]
})
</script>

<template>
  <AppPage id="live" :title="t('finish.live.title')">
    <template #actions>
      <UBadge
        v-if="status?.mode === 'Recording' && status.raceName"
        icon="i-lucide-flag"
        color="neutral"
        variant="subtle"
        size="lg"
        :label="status.raceName"
        data-testid="race-badge"
      />
      <UBadge v-if="status" :color="modeColor(status.mode)" variant="subtle" size="lg" :label="t(`finish.mode.${status.mode}`)" />
    </template>
    <template #toolbar>
      <UDashboardToolbar>
        <div class="flex flex-wrap gap-2">
          <UButton
            icon="i-lucide-eye"
            :label="t('finish.live.startPreview')"
            :variant="status?.mode === 'Preview' ? 'solid' : 'outline'"
            :disabled="control.busy"
            @click="start('Preview')"
          />
          <UButton
            icon="i-lucide-circle-dot"
            color="error"
            :label="t('finish.live.startRecording')"
            :variant="status?.mode === 'Recording' ? 'solid' : 'outline'"
            :disabled="control.busy"
            @click="askRace"
          />
          <UButton
            icon="i-lucide-square"
            color="neutral"
            variant="outline"
            :label="t('finish.live.stop')"
            :disabled="control.busy || !running"
            @click="stop"
          />
          <UButton
            icon="i-lucide-hand"
            color="warning"
            :variant="status?.line.manualTrigger ? 'solid' : 'outline'"
            :label="status?.line.manualTrigger ? t('finish.live.triggerOff') : t('finish.live.triggerOn')"
            :aria-pressed="status?.line.manualTrigger ?? false"
            :disabled="control.busy"
            @click="toggleTrigger"
          />
          <UButton
            icon="i-lucide-refresh-ccw"
            color="neutral"
            variant="outline"
            :label="t('finish.live.relearn')"
            :disabled="control.busy || !running"
            @click="relearn"
          />
        </div>
      </UDashboardToolbar>
    </template>

    <UAlert v-if="!connected" color="warning" variant="subtle" icon="i-lucide-wifi-off" :title="t('finish.live.disconnected')" class="mb-4" />

    <div v-if="status" class="grid gap-4 xl:grid-cols-2">
      <LivePreview
        kind="FinishCamera"
        :title="t('finish.live.finishCamera')"
        :active="running"
        :overlay="status.overlay"
        :line-state="status.line.state"
      />
      <LivePreview kind="FrontCamera" :title="t('finish.live.frontCamera')" :active="running && status.frontCamera.state !== 'Disabled'" />
      <div class="xl:col-span-2">
        <LivePreview kind="FinishStrip" :title="t('finish.live.strip')" :active="running" stretch />
      </div>
    </div>

    <div v-if="status" class="mt-4 grid gap-4 md:grid-cols-3">
      <UCard v-for="c in cameras" :key="c.key">
        <template #header>
          <div class="flex items-center justify-between">
            <span class="font-medium">{{ c.title }}</span>
            <UBadge :color="cameraColor(c.camera.state)" variant="subtle" :label="t(`finish.cameraState.${c.camera.state}`)" />
          </div>
        </template>
        <dl class="grid grid-cols-2 gap-1 text-sm">
          <dt class="text-muted">{{ t('finish.live.frameRate') }}</dt>
          <dd class="mono" :data-testid="`${c.key}-fps`">
            {{ t('finish.live.perSecond', { value: c.camera.measuredFrameRate.toFixed(1) }) }}
            <span class="text-dimmed">({{ t('finish.live.configured', { value: c.camera.configuredFrameRate }) }})</span>
          </dd>
        </dl>
        <UAlert v-if="c.error" color="error" variant="subtle" :title="c.error" class="mt-2" role="alert" />
        <UAlert v-if="c.warning" color="warning" variant="subtle" icon="i-lucide-triangle-alert" :title="c.warning" class="mt-2" role="status" />
      </UCard>

      <UCard>
        <template #header>
          <div class="flex items-center justify-between">
            <span class="font-medium">{{ t('finish.live.lineRate') }}</span>
            <UBadge v-if="running" :color="lineColor(status.line.state)" variant="subtle" :label="t(`finish.lineState.${status.line.state}`)" />
          </div>
        </template>
        <dl class="grid grid-cols-2 gap-1 text-sm">
          <dt class="text-muted">{{ t('finish.live.lineRate') }}</dt>
          <dd class="mono">{{ status.line.lineRate == null ? '–' : t('finish.live.perSecond', { value: status.line.lineRate.toFixed(1) }) }}</dd>
          <dt class="text-muted">{{ t('finish.live.occupancy') }}</dt>
          <dd class="mono">{{ status.line.occupancyPercent.toFixed(1) }} %</dd>
          <dt class="text-muted">{{ t('finish.live.events') }}</dt>
          <dd class="mono">{{ status.line.detectedEvents }}</dd>
          <dt class="text-muted">{{ t('finish.live.pendingSaves') }}</dt>
          <dd class="mono">{{ status.pendingSaves }}</dd>
        </dl>
        <p v-if="status.line.backgroundLearning && running" class="mt-2 text-sm text-muted">{{ t('finish.live.learning') }}</p>
        <p v-if="status.line.eventRunning && status.line.eventStartedAt" class="mt-2 text-sm" role="status">
          {{ t('finish.live.eventRunning', { time: formatClock(status.line.eventStartedAt) }) }}
        </p>
        <UAlert v-if="status.line.lineRateWarning" color="warning" variant="subtle" icon="i-lucide-triangle-alert" :title="t('finish.live.lineRateWarning')" class="mt-2" role="alert" />
      </UCard>
    </div>

    <UAlert
      v-if="status?.lastProblem"
      color="error"
      variant="subtle"
      icon="i-lucide-circle-alert"
      class="mt-4"
      :title="t('finish.live.lastProblem')"
      :description="`${formatClock(status.lastProblem.at)} – ${errorText(status.lastProblem.code)}`"
    />
    <RaceDialog v-model:open="raceDialogOpen" :initial-name="raceInitial" :busy="control.busy" :error="raceError" @confirm="startRecording" />
  </AppPage>
</template>
