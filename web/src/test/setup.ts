import { mount, type ComponentMountingOptions } from '@vue/test-utils'
import { createPinia, setActivePinia } from 'pinia'
import ui from '@nuxt/ui/vue-plugin'
import { vi } from 'vitest'
import type { Component } from 'vue'
import { i18n } from '@/i18n'

export interface FakeResponse {
  status?: number
  body?: unknown
}

/** Fake backend at the HTTP boundary: routes "METHOD /path" to canned responses and records requests. */
export function fakeBackend(routes: Record<string, FakeResponse | ((body: unknown) => FakeResponse)>) {
  const requests: { method: string; path: string; body: unknown }[] = []
  const fetchMock = vi.fn(async (input: RequestInfo | URL, init?: RequestInit) => {
    const request = input instanceof Request ? input : new Request(new URL(String(input), 'http://localhost'), init)
    const url = new URL(request.url)
    const text = await request.text()
    const body: unknown = text ? JSON.parse(text) : undefined
    requests.push({ method: request.method, path: url.pathname, body })
    const route = routes[`${request.method} ${url.pathname}`]
    const response = typeof route === 'function' ? route(body) : (route ?? { status: 404, body: { code: 'http.404' } })
    const status = response.status ?? 200
    return new Response(status === 204 ? null : JSON.stringify(response.body ?? {}), {
      status,
      headers: { 'Content-Type': status >= 400 ? 'application/problem+json' : 'application/json' },
    })
  })
  vi.stubGlobal('fetch', fetchMock)
  return { requests }
}

export function mountWithApp<T extends Component>(component: T, options: ComponentMountingOptions<T> = {}) {
  const pinia = createPinia()
  setActivePinia(pinia)
  i18n.global.locale.value = 'de'
  return mount(component, {
    ...options,
    attachTo: document.body,
    global: {
      ...options.global,
      plugins: [pinia, i18n, ui, ...(options.global?.plugins ?? [])],
    },
  } as ComponentMountingOptions<T>)
}

export const flush = () => new Promise((resolve) => setTimeout(resolve, 0))
