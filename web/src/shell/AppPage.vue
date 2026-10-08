<script setup lang="ts">
/** Page frame of the dashboard: navbar with title and actions, scrollable body. */
defineProps<{ id: string; title: string }>()
</script>

<template>
  <UDashboardPanel :id="id">
    <template #header>
      <UDashboardNavbar :title="title">
        <template #leading>
          <UDashboardSidebarCollapse />
          <slot name="leading" />
        </template>
        <template #right>
          <slot name="actions" />
        </template>
      </UDashboardNavbar>
      <slot name="toolbar" />
    </template>
    <template #body>
      <!-- One wrapper: direct children of the flex body (e.g. alerts with hidden overflow) would otherwise shrink. -->
      <div>
        <slot />
      </div>
    </template>
  </UDashboardPanel>
</template>
