import { useI18n } from 'vue-i18n'
import { useToast } from '@nuxt/ui/composables'
import { ApiError } from '@/api/client'

/** Translates backend error codes; unknown codes fall back to a generic message with the code. */
export function useErrorMessage() {
  const { t, te } = useI18n()
  const toast = useToast()

  function messageOf(error: unknown): string {
    if (error instanceof ApiError) {
      const key = `errors.${error.code}`
      return te(key) ? t(key, error.params) : t('errors.unknown', { code: error.code })
    }
    return error instanceof Error ? error.message : t('errors.unknown', { code: String(error) })
  }

  function showError(error: unknown): void {
    toast.add({
      title: t('common.error'),
      description: messageOf(error),
      color: 'error',
      icon: 'i-lucide-circle-alert',
    })
  }

  function showSuccess(description: string): void {
    toast.add({ title: t('common.done'), description, color: 'success', icon: 'i-lucide-circle-check', duration: 2500 })
  }

  return { messageOf, showError, showSuccess }
}
