<script setup lang="ts">
import { reactive, ref } from 'vue'
import { useRouter } from 'vue-router'
import { ElMessage } from 'element-plus'
import { http, refreshCsrf } from '../api/http'
import { errorMessage } from '../api/problems'
import { useAuthStore } from '../stores/auth'
import PageHeader from '../components/PageHeader.vue'
const auth = useAuthStore()
const router = useRouter()
const password = reactive({ currentPassword: '', newPassword: '' })
const setup = ref<{ key: string; uri: string } | null>(null)
const code = ref('')
const recoveryCodes = ref<string[]>([])
const busy = ref(false)
async function changePassword() {
  if (password.newPassword.length < 15) {
    ElMessage.warning('新密码至少需要 15 个字符。')
    return
  }
  busy.value = true
  try {
    await refreshCsrf()
    await http.post('/auth/password', password)
    password.currentPassword = ''
    password.newPassword = ''
    auth.clear()
    ElMessage.success('密码已更新，请重新登录。')
    await router.replace('/login')
  } catch (error) {
    ElMessage.error(errorMessage(error))
  } finally {
    busy.value = false
  }
}
async function setupMfa() {
  try {
    await refreshCsrf()
    setup.value = (await http.post<{ key: string; uri: string }>('/auth/mfa/setup', {})).data
    await refreshCsrf()
  } catch (error) {
    ElMessage.error(errorMessage(error))
  }
}
async function enableMfa() {
  try {
    recoveryCodes.value = (
      await http.post<{ recoveryCodes: string[] }>('/auth/mfa/enable', { code: code.value })
    ).data.recoveryCodes
    setup.value = null
    code.value = ''
    await refreshCsrf()
    await auth.load()
    ElMessage.success('多因素认证已启用，请妥善保存恢复码。')
  } catch (error) {
    ElMessage.error(errorMessage(error))
  }
}
</script>
<template>
  <PageHeader
    title="账号安全"
    description="保护全局账号。密码与多因素认证设置对所有工作空间生效。"
  />
  <ElAlert
    v-if="auth.profile?.mfaSetupRequired"
    title="正式环境的管理员需先启用多因素认证。"
    type="warning"
    :closable="false"
    class="space-bottom"
  />
  <div class="security-grid">
    <section class="panel">
      <h3>更改密码</h3>
      <p class="muted">使用 15～128 个字符的独立密码，避免常见弱密码。</p>
      <ElForm label-position="top" @submit.prevent="changePassword"
        ><ElFormItem label="当前密码"
          ><ElInput
            v-model="password.currentPassword"
            type="password"
            show-password
            autocomplete="current-password" /></ElFormItem
        ><ElFormItem label="新密码"
          ><ElInput
            v-model="password.newPassword"
            type="password"
            show-password
            autocomplete="new-password"
            maxlength="128" /></ElFormItem
        ><ElButton type="primary" :loading="busy" native-type="submit">更新密码</ElButton></ElForm
      >
    </section>
    <section class="panel">
      <h3>多因素认证</h3>
      <p class="muted">用认证器生成动态验证码，为账号增加一层保护。</p>
      <ElTag v-if="auth.profile?.twoFactorEnabled" type="success">已启用</ElTag
      ><ElButton v-else-if="!setup" type="primary" plain @click="setupMfa">开始设置</ElButton>
      <div v-if="setup">
        <p>在认证器中添加 Qizhu 账号，并输入以下密钥：</p>
        <div class="secret-display">{{ setup.key }}</div>
        <ElInput
          v-model="code"
          maxlength="6"
          placeholder="认证器中的 6 位验证码"
          inputmode="numeric"
          class="space-bottom"
        /><ElButton type="primary" @click="enableMfa">验证并启用</ElButton>
      </div>
      <div v-if="recoveryCodes.length">
        <ElAlert title="恢复码只展示这一次，请保存到安全位置。" type="warning" :closable="false" />
        <div class="recovery-list">
          <code v-for="recoveryCode in recoveryCodes" :key="recoveryCode">{{ recoveryCode }}</code>
        </div>
        <ElButton @click="recoveryCodes = []">我已安全保存</ElButton>
      </div>
    </section>
  </div>
</template>
