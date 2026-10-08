<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import AppPage from '@/shell/AppPage.vue'
import { api, unwrap, type Schemas } from '@/api/client'
import { useErrorMessage } from '@/shared/useErrorMessage'
import { useControlStore } from './controlStore'
import {
  deviceOptions,
  modeOptions,
  modesOf,
  selectDevice,
  selectedKey,
  selectedModeKey,
  selectMode,
} from './deviceSelection'

type Settings = Schemas['SettingsDto']
type CameraKey = 'finishCamera' | 'frontCamera'

const { t } = useI18n()
const control = useControlStore()
const { messageOf, showSuccess } = useErrorMessage()

const settings = ref<Settings | null>(null)
const effectiveMediaDirectory = ref('')
const appliesOnNextStart = ref(false)
const devices = ref<Schemas['CameraDevice'][]>([])
const searchingDevices = ref(false)
const error = ref<string | null>(null)
const saving = ref(false)

const camerasRunning = computed(() => appliesOnNextStart.value || (control.status?.mode ?? 'Stopped') !== 'Stopped')

function apply(response: Schemas['SettingsResponse']): void {
  settings.value = structuredClone(response.settings)
  effectiveMediaDirectory.value = response.effectiveMediaDirectory
  appliesOnNextStart.value = response.appliesOnNextStart
}

onMounted(async () => {
  try {
    apply(await unwrap(api.GET('/api/settings')))
  } catch (e) {
    error.value = messageOf(e)
  }
})

async function loadDevices(): Promise<void> {
  searchingDevices.value = true
  error.value = null
  try {
    devices.value = await unwrap(api.GET('/api/cameras/devices'))
  } catch (e) {
    error.value = messageOf(e)
  } finally {
    searchingDevices.value = false
  }
}

async function save(): Promise<void> {
  if (!settings.value) {
    return
  }
  saving.value = true
  error.value = null
  try {
    apply(await unwrap(api.PUT('/api/settings', { body: settings.value })))
    showSuccess(t('finish.settings.saved'))
  } catch (e) {
    error.value = messageOf(e)
  } finally {
    saving.value = false
  }
}

function autoExposure(camera: CameraKey): boolean {
  return settings.value?.[camera].exposure == null
}

function setAutoExposure(camera: CameraKey, auto: boolean | 'indeterminate'): void {
  if (settings.value) {
    settings.value[camera].exposure = auto === true ? null : -6
  }
}

const deviceLabels = computed(() => ({
  byIndex: (index: number) => t('finish.settings.deviceByIndex', { index }),
  inUse: t('finish.settings.inUse'),
  notListed: (name: string) => t('finish.settings.deviceNotListed', { name }),
}))

const modeLabels = computed(() => ({
  mode: (m: Schemas['CameraMode']) =>
    t('finish.settings.modeLabel', { width: m.width, height: m.height, fps: Math.round(m.frameRate), format: m.format }),
  custom: (m: { width: number; height: number; frameRate: number }) =>
    t('finish.settings.modeCustom', { width: m.width, height: m.height, fps: m.frameRate }),
}))

function chooseDevice(camera: CameraKey, key: string): void {
  if (settings.value) {
    selectDevice(settings.value[camera], key, devices.value, camera === 'finishCamera' ? 'finish' : 'front')
  }
}

function chooseMode(camera: CameraKey, key: string): void {
  if (settings.value) {
    selectMode(settings.value[camera], key, modesOf(settings.value[camera], devices.value))
  }
}

/** Hint only: a changed resolution can move the finish line out of the image; saving validates it. */
const lineOutsideMax = computed(() => {
  if (!settings.value) {
    return null
  }
  const { finishLine, finishCamera } = settings.value
  const turned = finishLine.rotation === 'Clockwise90' || finishLine.rotation === 'CounterClockwise90'
  const extent = turned ? finishCamera.height : finishCamera.width
  return finishLine.position + finishLine.width > extent ? extent - finishLine.width : null
})

/** Empty input means the default directory (null in the DTO). */
const mediaDirectory = computed({
  get: () => settings.value?.mediaDirectory ?? '',
  set: (value: string) => {
    if (settings.value) {
      settings.value.mediaDirectory = value.trim() === '' ? null : value
    }
  },
})

const rotationItems = computed(() =>
  (['None', 'Clockwise90', 'CounterClockwise90', 'Rotate180'] as const).map((value) => ({
    value,
    label: t(`finish.settings.rotations.${value}`),
  })),
)

const cameraSections = computed(() => [
  { key: 'finishCamera' as const, title: t('finish.settings.finishCamera') },
  { key: 'frontCamera' as const, title: t('finish.settings.frontCamera') },
])
</script>

<template>
  <AppPage id="settings" :title="t('finish.settings.title')">
    <template #actions>
      <UButton icon="i-lucide-save" :label="t('common.save')" :loading="saving" :disabled="!settings" @click="save" />
    </template>

    <UAlert
      v-if="camerasRunning"
      color="info"
      variant="subtle"
      icon="i-lucide-info"
      :title="t('finish.settings.appliesOnNextStart')"
      class="mb-4"
      role="status"
    />
    <UAlert v-if="error" color="error" variant="subtle" :title="error" class="mb-4" role="alert" />

    <form v-if="settings" class="grid gap-4 xl:grid-cols-2" @submit.prevent="save">
      <UCard>
        <template #header>{{ t('finish.settings.storage') }}</template>
        <UFormField :label="t('finish.settings.mediaDirectory')" :help="t('finish.settings.mediaDirectoryHint', { path: effectiveMediaDirectory })">
          <UInput v-model="mediaDirectory" class="w-full" :placeholder="effectiveMediaDirectory" />
        </UFormField>
      </UCard>

      <UCard>
        <template #header>
          <div class="flex items-center justify-between">
            <span>{{ t('finish.settings.device') }}</span>
            <UButton
              size="xs"
              icon="i-lucide-search"
              color="neutral"
              variant="outline"
              :label="t('finish.settings.devicesReload')"
              :loading="searchingDevices"
              @click="loadDevices"
            />
          </div>
        </template>
        <p v-if="searchingDevices" role="status" class="text-sm text-muted">{{ t('finish.settings.devicesSearching') }}</p>
        <p v-else-if="devices.length === 0" class="text-sm text-muted">{{ t('finish.settings.devicesHint') }}</p>
        <ul class="text-sm">
          <li v-for="d in devices" :key="d.index" class="mono">
            {{ d.index }}: {{ d.name }}<span v-if="d.inUse" class="text-dimmed"> ({{ t('finish.settings.inUse') }})</span>
          </li>
        </ul>
      </UCard>

      <UCard v-for="section in cameraSections" :key="section.key">
        <template #header>{{ section.title }}</template>
        <div class="grid grid-cols-2 gap-3">
          <UCheckbox
            v-if="section.key === 'frontCamera'"
            v-model="settings.frontCameraEnabled"
            :label="t('finish.settings.frontEnabled')"
            class="col-span-2"
          />
          <UFormField :label="t('finish.settings.camera')" class="col-span-2">
            <USelect
              :model-value="selectedKey(settings[section.key])"
              :items="deviceOptions(settings[section.key], devices, deviceLabels)"
              class="w-full"
              @update:model-value="chooseDevice(section.key, $event)"
            />
          </UFormField>
          <UFormField v-if="modesOf(settings[section.key], devices).length > 0" :label="t('finish.settings.mode')" class="col-span-2">
            <USelect
              :model-value="selectedModeKey(settings[section.key])"
              :items="modeOptions(settings[section.key], modesOf(settings[section.key], devices), modeLabels)"
              class="w-full"
              @update:model-value="chooseMode(section.key, $event)"
            />
          </UFormField>
          <UFormField :label="t('finish.settings.width')">
            <UInput v-model.number="settings[section.key].width" type="number" class="w-full" />
          </UFormField>
          <UFormField :label="t('finish.settings.height')">
            <UInput v-model.number="settings[section.key].height" type="number" class="w-full" />
          </UFormField>
          <UFormField :label="t('finish.settings.frameRate')">
            <UInput v-model.number="settings[section.key].frameRate" type="number" min="1" class="w-full" />
          </UFormField>
          <UFormField :label="t('finish.settings.offset')">
            <UInput v-model.number="settings[section.key].offsetMs" type="number" class="w-full" />
          </UFormField>
          <UCheckbox
            :model-value="autoExposure(section.key)"
            :label="t('finish.settings.autoExposure')"
            class="col-span-2"
            @update:model-value="setAutoExposure(section.key, $event)"
          />
          <UFormField v-if="!autoExposure(section.key)" :label="t('finish.settings.exposure')">
            <UInput v-model.number="settings[section.key].exposure" type="number" step="any" class="w-full" />
          </UFormField>
        </div>
      </UCard>

      <UCard>
        <template #header>{{ t('finish.settings.finishLine') }}</template>
        <UAlert
          v-if="lineOutsideMax !== null"
          color="warning"
          variant="subtle"
          icon="i-lucide-triangle-alert"
          :title="t('finish.settings.lineOutside', { max: lineOutsideMax })"
          class="mb-3"
          role="status"
        />
        <div class="grid grid-cols-2 gap-3">
          <UFormField :label="t('finish.settings.rotation')" :help="t('finish.settings.rotationHint')" class="col-span-2">
            <URadioGroup v-model="settings.finishLine.rotation" :items="rotationItems" orientation="horizontal" />
          </UFormField>
          <UFormField :label="t('finish.settings.position')">
            <UInput v-model.number="settings.finishLine.position" type="number" min="0" class="w-full" />
          </UFormField>
          <UFormField :label="t('finish.settings.lineWidth')">
            <UInput v-model.number="settings.finishLine.width" type="number" min="1" class="w-full" />
          </UFormField>
          <UCheckbox v-model="settings.finishLine.reverseTimeDirection" :label="t('finish.settings.reverse')" class="col-span-2" />
        </div>
      </UCard>

      <UCard>
        <template #header>{{ t('finish.settings.detection') }}</template>
        <div class="grid grid-cols-2 gap-3">
          <UFormField :label="t('finish.settings.pixelThreshold')">
            <UInput v-model.number="settings.detection.pixelThreshold" type="number" class="w-full" />
          </UFormField>
          <UFormField :label="t('finish.settings.occupancyThreshold')">
            <UInput v-model.number="settings.detection.occupancyThresholdPercent" type="number" step="0.1" class="w-full" />
          </UFormField>
          <UFormField :label="t('finish.settings.preRoll')">
            <UInput v-model.number="settings.detection.preRollSeconds" type="number" step="0.1" class="w-full" />
          </UFormField>
          <UFormField :label="t('finish.settings.postRoll')">
            <UInput v-model.number="settings.detection.postRollSeconds" type="number" step="0.1" class="w-full" />
          </UFormField>
          <UFormField :label="t('finish.settings.maxDuration')">
            <UInput v-model.number="settings.detection.maxDurationSeconds" type="number" class="w-full" />
          </UFormField>
          <span />
          <UFormField :label="t('finish.settings.frontPreRoll')">
            <UInput v-model.number="settings.detection.frontPreRollSeconds" type="number" step="0.1" class="w-full" />
          </UFormField>
          <UFormField :label="t('finish.settings.frontPostRoll')">
            <UInput v-model.number="settings.detection.frontPostRollSeconds" type="number" step="0.1" class="w-full" />
          </UFormField>
        </div>
      </UCard>
    </form>
  </AppPage>
</template>
