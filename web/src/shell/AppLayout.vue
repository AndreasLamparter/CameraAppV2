<script setup lang="ts">
import { computed, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import type { NavigationMenuItem } from '@nuxt/ui'
import UserMenu from './UserMenu.vue'
import ModeBadge from '@/features/finishRecording/ModeBadge.vue'

const { t } = useI18n()
const open = ref(false)

function link(label: string, icon: string, to: string): NavigationMenuItem {
  return { label, icon, to, onSelect: () => (open.value = false) }
}

const links = computed<NavigationMenuItem[]>(() => [
  link(t('common.nav.live'), 'i-lucide-video', '/live'),
  link(t('common.nav.recordings'), 'i-lucide-film', '/recordings'),
  link(t('common.nav.settings'), 'i-lucide-settings', '/settings'),
])
</script>

<template>
  <UDashboardGroup unit="rem" storage="local">
    <UDashboardSidebar
      id="default"
      v-model:open="open"
      collapsible
      resizable
      class="bg-elevated/25"
      :ui="{ footer: 'lg:border-t lg:border-default' }"
    >
      <template #header="{ collapsed }">
        <RouterLink to="/live" class="flex items-center gap-2 px-1 py-2">
          <UIcon name="i-lucide-flag-triangle-right" class="size-6 shrink-0 text-primary" />
          <span v-if="!collapsed" class="text-lg font-semibold text-highlighted">{{ t('common.appName') }}</span>
        </RouterLink>
      </template>

      <template #default="{ collapsed }">
        <UNavigationMenu :collapsed="collapsed" :items="links" orientation="vertical" tooltip />
        <div v-if="!collapsed" class="mt-auto">
          <ModeBadge />
        </div>
      </template>

      <template #footer="{ collapsed }">
        <UserMenu :collapsed="collapsed" />
      </template>
    </UDashboardSidebar>

    <RouterView />
  </UDashboardGroup>
</template>
