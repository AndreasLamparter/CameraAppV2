<script setup lang="ts">
import { useConfirmState } from './confirm'

const state = useConfirmState()

function close(confirmed: boolean): void {
  const resolve = state.resolve
  state.open = false
  state.resolve = undefined
  resolve?.(confirmed)
}
</script>

<template>
  <UModal :open="state.open" :title="state.title" @update:open="(open: boolean) => !open && close(false)">
    <template #body>
      <p>{{ state.message }}</p>
    </template>
    <template #footer>
      <div class="flex w-full justify-end gap-2">
        <UButton :label="state.cancelLabel" color="neutral" variant="ghost" @click="close(false)" />
        <UButton :label="state.confirmLabel" :color="state.danger ? 'error' : 'primary'" @click="close(true)" />
      </div>
    </template>
  </UModal>
</template>
