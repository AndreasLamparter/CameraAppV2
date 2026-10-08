<script setup lang="ts">
import { ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute, useRouter } from 'vue-router'
import { useSessionStore } from './sessionStore'
import { useErrorMessage } from '@/shared/useErrorMessage'

const { t } = useI18n()
const session = useSessionStore()
const router = useRouter()
const route = useRoute()
const { messageOf } = useErrorMessage()

const pin = ref('')
const error = ref<string | null>(null)
const busy = ref(false)

async function submit(): Promise<void> {
  busy.value = true
  error.value = null
  try {
    await session.login(pin.value)
    const redirect = route.query.redirect
    // Only app-internal paths: no open redirect to other origins.
    const target =
      typeof redirect === 'string' && redirect.startsWith('/') && !redirect.startsWith('//') ? redirect : '/'
    await router.replace(target)
  } catch (e) {
    error.value = messageOf(e)
  } finally {
    busy.value = false
    pin.value = ''
  }
}
</script>

<template>
  <main class="grid min-h-screen place-items-center p-4">
    <UCard class="w-full max-w-sm">
      <template #header>
        <h1 class="text-xl font-semibold text-highlighted">{{ t('access.title') }}</h1>
      </template>
      <form class="flex flex-col gap-4" @submit.prevent="submit">
        <UFormField :label="t('access.pin')">
          <UInput v-model="pin" type="password" autocomplete="current-password" autofocus class="w-full" />
        </UFormField>
        <UAlert v-if="error" color="error" variant="subtle" :title="error" role="alert" />
        <UButton type="submit" :label="t('access.login')" :loading="busy" block />
      </form>
    </UCard>
  </main>
</template>
