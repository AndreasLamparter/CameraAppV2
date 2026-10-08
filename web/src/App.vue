<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRoute } from 'vue-router'
import * as uiLocales from '@nuxt/ui/locale'
import { useSessionStore } from '@/features/access/sessionStore'
import AppLayout from '@/shell/AppLayout.vue'
import ConfirmHost from '@/shared/ConfirmHost.vue'

const { locale } = useI18n()
const session = useSessionStore()
const route = useRoute()

const showShell = computed(() => !route.meta.public && session.signedIn)
const uiLocale = computed(() => (locale.value === 'en' ? uiLocales.en : uiLocales.de))
</script>

<template>
  <UApp :locale="uiLocale" :toaster="{ position: 'bottom-right' }">
    <AppLayout v-if="showShell" />
    <RouterView v-else />
    <ConfirmHost />
  </UApp>
</template>
