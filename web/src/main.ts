import { createApp } from 'vue'
import { createPinia } from 'pinia'
import ui from '@nuxt/ui/vue-plugin'
import './styles.css'
import App from './App.vue'
import { router } from './router'
import { i18n } from './i18n'
import { onUnauthorized } from './api/client'
import { useSessionStore } from './features/access/sessionStore'

const app = createApp(App)
const pinia = createPinia()
app.use(pinia)
app.use(router)
app.use(i18n)
app.use(ui)

onUnauthorized(() => {
  const session = useSessionStore(pinia)
  if (session.signedIn) {
    session.expired()
    void router.push({ name: 'login', query: { redirect: router.currentRoute.value.fullPath } })
  }
})

app.mount('#app')
