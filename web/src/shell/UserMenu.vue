<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { useColorMode } from '@vueuse/core'
import type { DropdownMenuItem } from '@nuxt/ui'
import { useSessionStore } from '@/features/access/sessionStore'
import { setLocale, type Locale } from '@/i18n'

defineProps<{ collapsed?: boolean }>()

const { t, locale } = useI18n()
const session = useSessionStore()
const router = useRouter()
// Same storage key as Nuxt UI's own color mode: the choice is remembered per device.
const { store: appearance } = useColorMode()

type Appearance = 'light' | 'dark' | 'auto'

function appearanceItem(mode: Appearance, icon: string): DropdownMenuItem {
  return {
    label: t(`common.appearance.${mode}`),
    icon,
    type: 'checkbox',
    checked: appearance.value === mode,
    onSelect(e: Event) {
      e.preventDefault()
      appearance.value = mode
    },
  }
}

function languageItem(value: Locale): DropdownMenuItem {
  return {
    label: t(`common.languages.${value}`),
    type: 'checkbox',
    checked: locale.value === value,
    onSelect(e: Event) {
      e.preventDefault()
      setLocale(value)
    },
  }
}

const items = computed<DropdownMenuItem[][]>(() => [
  [{ type: 'label', label: t('common.operator'), icon: 'i-lucide-user-round' }],
  [
    {
      label: t('common.appearance.title'),
      icon: 'i-lucide-sun-moon',
      children: [
        appearanceItem('light', 'i-lucide-sun'),
        appearanceItem('dark', 'i-lucide-moon'),
        appearanceItem('auto', 'i-lucide-monitor'),
      ],
    },
    {
      label: t('common.language'),
      icon: 'i-lucide-languages',
      children: [languageItem('de'), languageItem('en')],
    },
  ],
  [
    {
      label: t('access.logout'),
      icon: 'i-lucide-log-out',
      async onSelect() {
        await session.logout()
        await router.push({ name: 'login' })
      },
    },
  ],
])
</script>

<template>
  <UDropdownMenu
    :items="items"
    :content="{ align: 'center', collisionPadding: 12 }"
    :ui="{ content: collapsed ? 'w-48' : 'w-(--reka-dropdown-menu-trigger-width)' }"
  >
    <UButton
      :label="collapsed ? undefined : t('common.operator')"
      icon="i-lucide-circle-user-round"
      :trailing-icon="collapsed ? undefined : 'i-lucide-chevrons-up-down'"
      :aria-label="t('common.userMenu')"
      color="neutral"
      variant="ghost"
      block
      :square="collapsed"
      class="data-[state=open]:bg-elevated"
      :ui="{ trailingIcon: 'text-dimmed' }"
    />
  </UDropdownMenu>
</template>
