<script setup lang="ts">
defineProps<{ title: string; description: string; label?: string }>()
</script>
<template>
  <header class="page-heading">
    <div>
      <p class="eyebrow">{{ label ?? 'QIZHU WORKSPACE' }}</p>
      <h1>{{ title }}</h1>
      <p class="muted">{{ description }}</p>
    </div>
    <div class="heading-actions"><slot /></div>
  </header>
</template>
