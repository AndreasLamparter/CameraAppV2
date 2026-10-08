<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { api, unwrap, type Schemas } from '@/api/client'
import { copyText } from '@/shared/clipboard'
import { useErrorMessage } from '@/shared/useErrorMessage'
import { baseUrlOptions, controlEndpoints, curlCommand, endpointUrl, type ControlEndpoint } from './externalControl'

/** Endpoints for the timing system (FS1-50) with their URLs on this finish PC, ready to copy. */
const { t } = useI18n()
const { showError, showSuccess } = useErrorMessage()

const info = ref<Schemas['ExternalControlInfo'] | null>(null)
const baseUrl = ref('')
const showKey = ref(false)

const baseUrls = computed(() => baseUrlOptions(info.value?.baseUrls ?? [], window.location.origin))
const header = computed(() => info.value?.apiKeyHeader ?? 'X-Api-Key')
const apiKey = computed(() => info.value?.apiKey ?? null)
const headerLine = computed(() => `${header.value}: ${apiKey.value ?? '<API-KEY>'}`)

onMounted(async () => {
  try {
    info.value = await unwrap(api.GET('/api/settings/external-control'))
    baseUrl.value = baseUrls.value[0] ?? window.location.origin
  } catch (e) {
    showError(e)
  }
})

async function copy(text: string): Promise<void> {
  if (await copyText(text)) {
    showSuccess(t('finish.settings.external.copied'))
  } else {
    showError(new Error(t('finish.settings.external.copyFailed')))
  }
}

const rows = computed(() =>
  controlEndpoints.map((endpoint: ControlEndpoint) => ({
    endpoint,
    title: t(`finish.settings.external.endpoints.${endpoint.key}`),
    url: endpointUrl(baseUrl.value, endpoint),
    curl: curlCommand(baseUrl.value, endpoint, header.value, apiKey.value),
  })),
)
</script>

<template>
  <UCard class="xl:col-span-2">
    <template #header>
      <div class="flex flex-wrap items-center justify-between gap-2">
        <span>{{ t('finish.settings.external.title') }}</span>
        <UBadge
          v-if="info"
          :color="info.enabled ? 'success' : 'warning'"
          variant="subtle"
          :label="info.enabled ? t('finish.settings.external.enabled') : t('finish.settings.external.disabled')"
        />
      </div>
    </template>

    <p class="mb-3 text-sm text-muted">{{ t('finish.settings.external.hint', { header }) }}</p>
    <UAlert
      v-if="info && !info.enabled"
      color="warning"
      variant="subtle"
      icon="i-lucide-key-round"
      :title="t('finish.settings.external.disabledHint')"
      class="mb-3"
    />

    <div class="mb-4 grid gap-3 md:grid-cols-2">
      <UFormField :label="t('finish.settings.external.address')" :help="t('finish.settings.external.addressHint')">
        <USelect v-model="baseUrl" :items="baseUrls" class="w-full" />
      </UFormField>
      <UFormField :label="t('finish.settings.external.apiKey')" :help="t('finish.settings.external.apiKeyHint', { header })">
        <div class="flex items-center gap-2">
          <code class="mono grow truncate rounded bg-elevated px-2 py-1 text-sm" data-testid="api-key">
            {{ apiKey ? (showKey ? apiKey : '•'.repeat(12)) : t('finish.settings.external.noKey') }}
          </code>
          <UButton
            v-if="apiKey"
            :icon="showKey ? 'i-lucide-eye-off' : 'i-lucide-eye'"
            size="xs"
            color="neutral"
            variant="outline"
            :aria-label="showKey ? t('finish.settings.external.hideKey') : t('finish.settings.external.showKey')"
            :aria-pressed="showKey"
            @click="showKey = !showKey"
          />
          <UButton
            v-if="apiKey"
            icon="i-lucide-copy"
            size="xs"
            color="neutral"
            variant="outline"
            :aria-label="t('finish.settings.external.copyKey')"
            @click="copy(apiKey)"
          />
          <UButton
            icon="i-lucide-copy-plus"
            size="xs"
            color="neutral"
            variant="outline"
            :label="t('finish.settings.external.header')"
            :aria-label="t('finish.settings.external.copyHeader')"
            @click="copy(headerLine)"
          />
        </div>
      </UFormField>
    </div>

    <ul class="divide-y divide-default">
      <li v-for="row in rows" :key="row.endpoint.key" class="flex flex-wrap items-center gap-2 py-2">
        <span class="w-48 shrink-0 text-sm">{{ row.title }}</span>
        <UBadge :label="row.endpoint.method" color="neutral" variant="outline" class="mono w-14 justify-center" />
        <code class="mono min-w-0 grow break-all text-sm">{{ row.url }}</code>
        <code v-if="row.endpoint.body" class="mono text-sm text-muted">{{ row.endpoint.body }}</code>
        <UButton
          icon="i-lucide-copy"
          size="xs"
          color="neutral"
          variant="outline"
          :label="t('finish.settings.external.copyUrl')"
          @click="copy(row.url)"
        />
        <UButton
          icon="i-lucide-terminal"
          size="xs"
          color="neutral"
          variant="outline"
          :label="t('finish.settings.external.copyCurl')"
          @click="copy(row.curl)"
        />
      </li>
    </ul>
  </UCard>
</template>
