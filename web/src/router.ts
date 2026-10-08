import { createRouter, createWebHistory } from 'vue-router'
import { useSessionStore } from '@/features/access/sessionStore'

export const router = createRouter({
  history: createWebHistory(),
  routes: [
    {
      path: '/login',
      name: 'login',
      component: () => import('@/features/access/LoginView.vue'),
      meta: { public: true },
    },
    { path: '/', redirect: '/live' },
    { path: '/live', name: 'live', component: () => import('@/features/finishRecording/LiveView.vue') },
    {
      path: '/recordings',
      name: 'recordings',
      component: () => import('@/features/finishRecording/RecordingsView.vue'),
    },
    {
      path: '/recordings/:id',
      name: 'playback',
      component: () => import('@/features/finishRecording/PlaybackView.vue'),
      props: true,
    },
    { path: '/settings', name: 'settings', component: () => import('@/features/finishRecording/SettingsView.vue') },
    { path: '/:pathMatch(.*)*', redirect: '/live' },
  ],
})

router.beforeEach(async (to) => {
  const session = useSessionStore()
  if (!session.checked) {
    await session.check()
  }
  if (!to.meta.public && !session.signedIn) {
    return { name: 'login', query: { redirect: to.fullPath } }
  }
  return true
})
