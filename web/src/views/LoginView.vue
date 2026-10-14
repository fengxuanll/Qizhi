<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { useAuthStore } from '../stores/auth'
import { errorMessage } from '../api/problems'
const auth = useAuthStore()
const router = useRouter()
const form = reactive({ userName: '', password: '', code: '', recoveryCode: '' })
const recoveryMode = ref(false)
const busy = ref(false)
const error = ref('')
async function submit() {
  if (!form.userName || !form.password) {
    error.value = '请输入账号和密码。'
    return
  }
  busy.value = true
  error.value = ''
  try {
    await auth.login(
      form.userName,
      form.password,
      recoveryMode.value ? undefined : form.code || undefined,
      recoveryMode.value ? form.recoveryCode || undefined : undefined,
    )
    form.password = ''
    form.recoveryCode = ''
    await router.replace(auth.profile?.mfaSetupRequired ? '/security' : '/')
  } catch (failure) {
    error.value = errorMessage(failure)
  } finally {
    busy.value = false
  }
}
</script>
<template>
  <div class="login-page">
    <section class="login-story">
      <div class="brand light">
        <span class="brand-mark">Q</span><span>Qizhu<small>开发基础平台</small></span>
      </div>
      <div class="login-intro">
        <p class="eyebrow">A BETTER START FOR EVERY PROJECT</p>
        <h1>把基础做好，<br />让业务生长。</h1>
        <p>统一的工作空间，清晰的权限边界。<br />从这里开始，构建你的下一个项目。</p>
        <div class="story-tags">
          <span>模块化基础</span><span>租户隔离</span><span>可追溯操作</span>
        </div>
      </div>
      <p class="login-copyright">Qizhu Workspace · 2026</p>
    </section>
    <section class="login-form-area">
      <div class="login-card">
        <p class="eyebrow">WELCOME BACK</p>
        <h2>登录工作空间</h2>
        <p class="muted">使用管理员提供的账号，继续你的工作。</p>
        <ElAlert
          v-if="error"
          :title="error"
          type="error"
          :closable="false"
          show-icon
          class="login-error"
        />
        <ElForm label-position="top" @submit.prevent="submit">
          <ElFormItem label="账号"
            ><ElInput
              v-model="form.userName"
              placeholder="请输入账号"
              autocomplete="username"
              size="large"
          /></ElFormItem>
          <ElFormItem label="密码"
            ><ElInput
              v-model="form.password"
              type="password"
              show-password
              placeholder="请输入密码"
              autocomplete="current-password"
              size="large"
          /></ElFormItem>
          <ElFormItem v-if="!recoveryMode" label="动态验证码（已开启多因素认证时填写）"
            ><ElInput
              v-model="form.code"
              maxlength="6"
              inputmode="numeric"
              autocomplete="one-time-code"
              placeholder="6 位验证码"
              size="large"
          /></ElFormItem>
          <ElFormItem v-else label="一次性恢复码"
            ><ElInput
              v-model="form.recoveryCode"
              maxlength="128"
              placeholder="请输入安全保存的恢复码"
              size="large"
          /></ElFormItem>
          <ElButton text size="small" @click="recoveryMode = !recoveryMode">{{
            recoveryMode ? '使用动态验证码' : '无法使用认证器？使用恢复码'
          }}</ElButton>
          <ElButton
            type="primary"
            native-type="submit"
            :loading="busy"
            class="login-submit"
            size="large"
            >进入工作空间</ElButton
          >
        </ElForm>
        <p class="login-hint">账号访问范围由管理员授权。</p>
      </div>
    </section>
  </div>
</template>
