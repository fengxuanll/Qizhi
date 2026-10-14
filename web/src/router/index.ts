import { createRouter, createWebHistory } from 'vue-router'
import { useAuthStore } from '../stores/auth'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      component: () => import('../views/LoginView.vue'),
      meta: { public: true },
    },
    {
      path: '/',
      component: () => import('../layouts/AdminLayout.vue'),
      children: [
        {
          path: '',
          component: () => import('../views/OverviewView.vue'),
          meta: { title: '工作空间' },
        },
        {
          path: 'projects',
          component: () => import('../views/ProjectsView.vue'),
          meta: { title: '项目管理', permission: 'projects.projects.read' },
        },
        {
          path: 'security',
          component: () => import('../views/SecurityView.vue'),
          meta: { title: '账号安全' },
        },
      ],
    },
    { path: '/:pathMatch(.*)*', redirect: '/' },
  ],
})
router.beforeEach(async (to) => {
  const auth = useAuthStore()
  if (!auth.loaded) {
    try {
      await auth.load()
    } catch {
      return to.path === '/login' ? true : '/login'
    }
  }
  if (!to.meta.public && !auth.profile) {
    return '/login'
  }
  if (to.path === '/login' && auth.profile) {
    return '/'
  }
  if (auth.profile?.mfaSetupRequired && to.path !== '/security') {
    return '/security'
  }
  if (typeof to.meta.permission === 'string' && !auth.can(to.meta.permission)) {
    return '/'
  }
  document.title = `${String(to.meta.title ?? '登录')} · Qizhu`
  return true
})
