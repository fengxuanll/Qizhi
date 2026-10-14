import { defineStore } from 'pinia'
import { computed, ref } from 'vue'
import { http, refreshCsrf, setTenantContext } from '../api/http'
import { ApiError } from '../api/problems'
import type { UserProfile } from '../types/contracts'

export const useAuthStore = defineStore('auth', () => {
  const profile = ref<UserProfile | null>(null)
  const loaded = ref(false)
  const currentTenant = computed(() =>
    profile.value?.tenants.find((item) => item.id === profile.value?.tenantId),
  )
  function apply(value: UserProfile | null) {
    profile.value = value
    setTenantContext(value?.tenantId ?? null)
  }
  function can(permission: string) {
    return profile.value?.permissions.includes(permission) ?? false
  }
  async function load() {
    try {
      apply((await http.get<UserProfile>('/auth/me')).data)
    } catch (error) {
      if (!(error instanceof ApiError && error.status === 401)) {
        throw error
      }
      apply(null)
    } finally {
      loaded.value = true
    }
  }
  async function login(userName: string, password: string, code?: string, recoveryCode?: string) {
    await refreshCsrf()
    apply(
      (
        await http.post<UserProfile>('/auth/login', {
          userName,
          password,
          ...(code ? { code } : {}),
          ...(recoveryCode ? { recoveryCode } : {}),
        })
      ).data,
    )
    await refreshCsrf()
    if (profile.value?.tenants.length === 1 && !profile.value.mfaSetupRequired) {
      await selectTenant(profile.value.tenants[0]!.id)
    }
    loaded.value = true
  }
  async function selectTenant(id: string) {
    await refreshCsrf()
    apply((await http.post<UserProfile>('/auth/tenant', { tenantId: id })).data)
    await refreshCsrf()
    const channel = new BroadcastChannel('qizhu-auth')
    channel.postMessage('tenant-changed')
    channel.close()
  }
  async function logout() {
    await http.post('/auth/logout', {})
    apply(null)
  }
  return {
    profile,
    loaded,
    currentTenant,
    can,
    load,
    login,
    selectTenant,
    logout,
    clear: () => apply(null),
  }
})
