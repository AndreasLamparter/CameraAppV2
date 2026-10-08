import { createI18n } from 'vue-i18n'
import common from './messages/common'
import errors from './messages/errors'
import access from './messages/access'
import finish from './messages/finish'

export type Locale = 'de' | 'en'

const areas = { common, errors, access, finish }

function messagesFor(locale: Locale) {
  return Object.fromEntries(Object.entries(areas).map(([area, m]) => [area, m[locale]]))
}

const stored = (() => {
  try {
    return localStorage.getItem('timingapp.locale')
  } catch {
    return null
  }
})()

export const i18n = createI18n({
  legacy: false,
  locale: stored === 'en' ? 'en' : 'de',
  fallbackLocale: 'de',
  messages: { de: messagesFor('de'), en: messagesFor('en') },
})

export function setLocale(locale: Locale): void {
  i18n.global.locale.value = locale
  try {
    localStorage.setItem('timingapp.locale', locale)
  } catch {
    // per-viewer convenience only
  }
}
