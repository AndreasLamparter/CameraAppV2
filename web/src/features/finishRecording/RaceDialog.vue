<script setup lang="ts">
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'

/**
 * Asks for the race name before recording starts (FS2-01). The backend validates the name (FS2-07); its error is
 * shown in the dialog so the operator can correct it.
 */
const props = defineProps<{
  open: boolean
  initialName: string
  busy: boolean
  error: string | null
}>()

const emit = defineEmits<{
  'update:open': [open: boolean]
  confirm: [name: string]
}>()

const { t } = useI18n()
const name = ref(props.initialName)

watch(
  () => props.open,
  (open) => {
    if (open) {
      name.value = props.initialName
    }
  },
)

function submit(): void {
  if (name.value.trim()) {
    emit('confirm', name.value.trim())
  }
}
</script>

<template>
  <UModal :open="open" :title="t('finish.race.title')" @update:open="(value: boolean) => emit('update:open', value)">
    <template #body>
      <form class="flex flex-col gap-3" @submit.prevent="submit">
        <UFormField :label="t('finish.race.name')" :help="t('finish.race.hint')" required>
          <UInput v-model="name" autofocus maxlength="120" class="w-full" data-testid="race-name" />
        </UFormField>
        <UAlert v-if="error" color="error" variant="subtle" :title="error" role="alert" />
      </form>
    </template>
    <template #footer>
      <div class="flex w-full justify-end gap-2">
        <UButton :label="t('common.cancel')" color="neutral" variant="ghost" @click="emit('update:open', false)" />
        <UButton
          icon="i-lucide-circle-dot"
          color="error"
          :label="t('finish.race.start')"
          :loading="busy"
          :disabled="!name.trim()"
          @click="submit"
        />
      </div>
    </template>
  </UModal>
</template>
