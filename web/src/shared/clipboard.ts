/**
 * Copies text to the clipboard. The Clipboard API exists only in secure contexts (HTTPS, localhost); the finish PC
 * is usually opened over plain HTTP by its IP address, so a temporary text area with the copy command is the fallback.
 */
export async function copyText(text: string): Promise<boolean> {
  if (window.isSecureContext && navigator.clipboard) {
    try {
      await navigator.clipboard.writeText(text)
      return true
    } catch {
      // Permission denied: fall through to the fallback.
    }
  }
  const area = document.createElement('textarea')
  area.value = text
  area.setAttribute('readonly', '')
  area.style.position = 'fixed'
  area.style.opacity = '0'
  document.body.appendChild(area)
  area.select()
  try {
    // Deprecated, but the only way to copy outside a secure context.
    return document.execCommand('copy')
  } finally {
    area.remove()
  }
}
