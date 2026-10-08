import { defineStore } from 'pinia'
import { ref } from 'vue'
import { api, unwrap } from '@/api/client'
import { stopLive } from '@/api/live'

/** Operator session. Authentication is an HttpOnly cookie; nothing secret is stored in the browser. */
export const useSessionStore = defineStore('session', () => {
  const signedIn = ref(false)
  const checked = ref(false)

  async function check(): Promise<void> {
    try {
      signedIn.value = (await unwrap(api.GET('/api/auth/me'))).signedIn
    } catch {
      signedIn.value = false
    } finally {
      checked.value = true
    }
  }

  async function login(pin: string): Promise<void> {
    signedIn.value = (await unwrap(api.POST('/api/auth/login', { body: { pin } }))).signedIn
  }

  async function logout(): Promise<void> {
    await unwrap(api.POST('/api/auth/logout'))
    await stopLive()
    signedIn.value = false
  }

  function expired(): void {
    signedIn.value = false
  }

  return { signedIn, checked, check, login, logout, expired }
})
