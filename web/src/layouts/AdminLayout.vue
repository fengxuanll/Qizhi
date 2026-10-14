<script setup lang="ts">
import { useRoute, useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { useAuthStore } from '../stores/auth'
import { errorMessage } from '../api/problems'
const auth = useAuthStore()
const route = useRoute()
const router = useRouter()
async function selectTenant(id: string) {
  try {
    await auth.selectTenant(id)
    await router.replace('/')
  } catch (error) {
    ElMessage.error(errorMessage(error))
  }
}
async function logout() {
  try {
    await auth.logout()
    await router.replace('/login')
  } catch (error) {
    ElMessage.error(errorMessage(error))
  }
}
</script>
<template>
  <div class="admin-shell">
    <aside class="sidebar">
      <RouterLink to="/" class="brand"
        ><span class="brand-mark">Q</span><span>Qizhu<small>开发基础平台</small></span></RouterLink
      >
      <p class="nav-label">工作空间</p>
      <nav>
        <RouterLink to="/" :class="{ active: route.path === '/' }"
          ><span class="nav-icon">⌂</span>概览</RouterLink
        >
        <RouterLink
          v-if="auth.can('projects.projects.read')"
          to="/projects"
          :class="{ active: route.path === '/projects' }"
          ><span class="nav-icon">▦</span>项目管理</RouterLink
        >
        <RouterLink to="/security" :class="{ active: route.path === '/security' }"
          ><span class="nav-icon">◇</span>账号安全</RouterLink
        >
      </nav>
      <div class="sidebar-bottom">
        <span class="status-dot" />工作空间独立隔离
        <p>清晰的基础，专注于业务。</p>
      </div>
    </aside>
    <div class="main-area">
      <header class="topbar">
        <span class="breadcrumb">工作空间 <span>/</span> {{ route.meta.title }}</span>
        <div class="topbar-right">
          <ElSelect
            :model-value="auth.profile?.tenantId ?? undefined"
            placeholder="选择工作空间"
            style="width: 210px"
            @change="selectTenant"
          >
            <ElOption
              v-for="tenant in auth.profile?.tenants"
              :key="tenant.id"
              :label="tenant.name"
              :value="tenant.id"
            />
          </ElSelect>
          <span class="avatar">{{ auth.profile?.displayName.slice(0, 1) ?? 'Q' }}</span>
          <span class="user-name">{{ auth.profile?.displayName }}</span>
          <ElButton text @click="logout">退出</ElButton>
        </div>
      </header>
      <main class="page-content"><RouterView /></main>
      <footer class="page-footer">Qizhu · 为每一个新项目提供可靠起点</footer>
    </div>
  </div>
</template>
