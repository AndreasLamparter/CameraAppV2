<script setup lang="ts">
import { useI18n } from 'vue-i18n'
import AppPage from '@/shell/AppPage.vue'
import { useLive } from '@/api/live'
import { useConfirm } from '@/shared/confirm'
import { useErrorMessage } from '@/shared/useErrorMessage'
import { formatBytes, formatDateTime, formatSeconds } from '@/shared/format'
import { useRecordingsStore, type RecordingSummary } from './recordingsStore'

const { t, locale } = useI18n()
const recordings = useRecordingsStore()
const confirm = useConfirm()
const { showError, showSuccess } = useErrorMessage()

useLive('recordingsChanged', () => void recordings.load().catch(showError), () => recordings.load().catch(() => undefined), 5000)

async function remove(recording: RecordingSummary): Promise<void> {
  const confirmed = await confirm({
    title: t('finish.recordings.deleteTitle'),
    message: t('finish.recordings.deleteMessage', { time: formatDateTime(recording.startedAt, locale.value) }),
    confirmLabel: t('common.delete'),
    cancelLabel: t('common.cancel'),
    danger: true,
  })
  if (!confirmed) {
    return
  }
  try {
    await recordings.remove(recording.id)
    showSuccess(t('finish.recordings.deleted'))
  } catch (e) {
    showError(e)
  }
}
</script>

<template>
  <AppPage id="recordings" :title="t('finish.recordings.title')">
    <p v-if="recordings.loaded && recordings.items.length === 0" class="text-muted">{{ t('finish.recordings.empty') }}</p>
    <table v-else class="w-full text-sm">
      <thead class="text-left text-muted">
        <tr>
          <th class="py-2">{{ t('finish.recordings.startedAt') }}</th>
          <th>{{ t('finish.recordings.duration') }}</th>
          <th>{{ t('finish.recordings.lineRate') }}</th>
          <th>{{ t('finish.recordings.frontVideo') }}</th>
          <th>{{ t('finish.recordings.size') }}</th>
          <th class="sr-only">{{ t('finish.recordings.open') }}</th>
        </tr>
      </thead>
      <tbody>
        <tr v-for="r in recordings.items" :key="r.id" class="border-t border-default">
          <td class="py-2">
            <RouterLink :to="{ name: 'playback', params: { id: r.id } }" class="font-medium text-primary hover:underline">
              {{ formatDateTime(r.startedAt, locale) }}
            </RouterLink>
            <UBadge v-if="r.hasVideoError" color="warning" variant="subtle" size="sm" class="ml-2" :label="t('finish.recordings.videoError')" />
          </td>
          <td class="mono">{{ formatSeconds(r.durationSeconds) }}</td>
          <td class="mono">{{ r.lineRate.toFixed(1) }} /s</td>
          <td>{{ r.hasFrontVideo ? t('common.yes') : t('common.no') }}</td>
          <td class="mono">{{ formatBytes(r.sizeBytes) }}</td>
          <td class="text-right">
            <UButton
              icon="i-lucide-trash-2"
              color="error"
              variant="ghost"
              :aria-label="`${t('common.delete')} ${formatDateTime(r.startedAt, locale)}`"
              @click="remove(r)"
            />
          </td>
        </tr>
      </tbody>
    </table>
  </AppPage>
</template>
