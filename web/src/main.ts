import { createApp } from 'vue'
import { createPinia } from 'pinia'
import {
  ElAlert,
  ElButton,
  ElDialog,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
  ElLoading,
} from 'element-plus'
import 'element-plus/dist/index.css'
import './styles/main.css'
import App from './App.vue'
import { router } from './router'
import { useAuthStore } from './stores/auth'

const app = createApp(App)
for (const [name, component] of Object.entries({
  ElAlert,
  ElButton,
  ElDialog,
  ElEmpty,
  ElForm,
  ElFormItem,
  ElInput,
  ElOption,
  ElPagination,
  ElSelect,
  ElTable,
  ElTableColumn,
  ElTag,
})) {
  app.component(name, component)
}
app.use(createPinia()).use(router).use(ElLoading).mount('#app')
window.addEventListener('qizhu:session-expired', () => {
  useAuthStore().clear()
  void router.replace('/login')
})
new BroadcastChannel('qizhu-auth').onmessage = () => window.location.reload()
