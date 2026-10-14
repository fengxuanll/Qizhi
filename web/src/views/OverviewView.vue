<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useAuthStore } from '../stores/auth'
import { projects } from '../api/projects'
import PageHeader from '../components/PageHeader.vue'
const auth = useAuthStore()
const projectCount = ref<number | null>(null)
onMounted(async () => {
  if (auth.can('projects.projects.read')) {
    projectCount.value = (await projects.list({ pageNumber: 1, pageSize: 1 })).totalCount
  }
})
</script>
<template>
  <PageHeader title="工作空间概览" description="从共同的基础开始，安心推进每一项工作。" />
  <ElAlert
    v-if="!auth.profile?.tenantId"
    title="请在右上角选择一个工作空间。"
    type="info"
    :closable="false"
    class="space-bottom"
  />
  <section class="welcome-banner">
    <div>
      <span class="welcome-label">HELLO, {{ auth.profile?.displayName }}</span>
      <h2>欢迎回到 {{ auth.currentTenant?.name ?? 'Qizhu' }}</h2>
      <p>你的项目、账号与授权，都在一个清晰的空间里。</p>
    </div>
    <span class="welcome-symbol">Q</span>
  </section>
  <div class="stat-grid">
    <article class="stat-card">
      <span>当前项目</span><strong>{{ projectCount ?? '—' }}</strong
      ><small>当前工作空间中的有效项目</small>
    </article>
    <article class="stat-card">
      <span>已授予权限</span><strong>{{ auth.profile?.permissions.length ?? 0 }}</strong
      ><small>仅包含当前工作空间的授权</small>
    </article>
    <article class="stat-card">
      <span>可进入空间</span><strong>{{ auth.profile?.tenants.length ?? 0 }}</strong
      ><small>由成员关系决定访问范围</small>
    </article>
    <article class="stat-card">
      <span>账号保护</span
      ><strong class="text-stat">{{
        auth.profile?.twoFactorEnabled ? '已增强' : '基础保护'
      }}</strong
      ><small>{{
        auth.profile?.twoFactorEnabled ? '已开启多因素认证' : '建议开启动态验证码'
      }}</small>
    </article>
  </div>
  <div class="overview-grid">
    <section class="panel">
      <h3>快速开始</h3>
      <RouterLink v-if="auth.can('projects.projects.read')" to="/projects" class="quick-link"
        ><span><b>管理项目</b><small>创建、查询与维护项目，支持回收站恢复。</small></span
        ><span>→</span></RouterLink
      ><RouterLink to="/security" class="quick-link"
        ><span><b>保护账号</b><small>更改密码，开启多因素认证。</small></span
        ><span>→</span></RouterLink
      >
    </section>
    <section class="panel">
      <h3>你的授权</h3>
      <p class="muted">所有操作由服务端再次校验。</p>
      <div class="permission-list">
        <ElTag
          v-for="permission in auth.profile?.permissions"
          :key="permission"
          type="info"
          effect="plain"
          >{{ permission }}</ElTag
        ><ElEmpty
          v-if="!auth.profile?.permissions.length"
          description="选择空间后查看授权"
          :image-size="65"
        />
      </div>
    </section>
  </div>
</template>
