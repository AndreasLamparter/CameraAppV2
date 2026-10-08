import { reactive } from 'vue'

export interface ConfirmRequest {
  title: string
  message: string
  confirmLabel: string
  cancelLabel: string
  danger?: boolean
}

interface ConfirmState extends ConfirmRequest {
  open: boolean
  resolve?: (confirmed: boolean) => void
}

const state = reactive<ConfirmState>({ open: false, title: '', message: '', confirmLabel: '', cancelLabel: '' })

/** State of the single confirmation dialog rendered by {@link ConfirmHost}. */
export function useConfirmState() {
  return state
}

/** Asks the operator to confirm; resolves to true when confirmed. */
export function useConfirm() {
  return (request: ConfirmRequest): Promise<boolean> =>
    new Promise((resolve) => {
      state.resolve?.(false)
      Object.assign(state, request, { open: true, resolve })
    })
}
