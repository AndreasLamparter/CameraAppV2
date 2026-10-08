/** A control endpoint for external programs such as the timing system (FS1-50). */
export interface ControlEndpoint {
  /** i18n key below `finish.settings.external.endpoints`. */
  key: string
  method: 'GET' | 'POST' | 'PUT'
  path: string
  body?: string
}

export const controlEndpoints: ControlEndpoint[] = [
  { key: 'startRecording', method: 'POST', path: '/api/control/start', body: '{"mode":"Recording","raceName":"Lauf 1"}' },
  { key: 'startPreview', method: 'POST', path: '/api/control/start', body: '{"mode":"Preview"}' },
  { key: 'stop', method: 'POST', path: '/api/control/stop' },
  { key: 'triggerOn', method: 'PUT', path: '/api/control/trigger', body: '{"active":true}' },
  { key: 'triggerOff', method: 'PUT', path: '/api/control/trigger', body: '{"active":false}' },
  { key: 'passage', method: 'POST', path: '/api/passages', body: '{"startNumber":"42","time":"2026-10-08T10:00:02.123+02:00"}' },
  { key: 'relearn', method: 'POST', path: '/api/control/background/relearn' },
  { key: 'status', method: 'GET', path: '/api/control/status' },
  { key: 'shutdown', method: 'POST', path: '/api/control/shutdown' },
]

export function endpointUrl(baseUrl: string, endpoint: ControlEndpoint): string {
  return `${baseUrl.replace(/\/+$/, '')}${endpoint.path}`
}

/** A curl call with the key, or a placeholder when none is configured. */
export function curlCommand(baseUrl: string, endpoint: ControlEndpoint, apiKeyHeader: string, apiKey?: string | null): string {
  const parts = [`curl -X ${endpoint.method} "${endpointUrl(baseUrl, endpoint)}"`, `-H "${apiKeyHeader}: ${apiKey ?? '<API-KEY>'}"`]
  if (endpoint.body) {
    parts.push(`-H "Content-Type: application/json"`, `-d '${endpoint.body}'`)
  }
  return parts.join(' ')
}

/** The base URLs of the finish PC, plus the address this page was opened with when it is not among them. */
export function baseUrlOptions(baseUrls: string[], pageOrigin: string): string[] {
  return baseUrls.includes(pageOrigin) ? baseUrls : [...baseUrls, pageOrigin]
}
