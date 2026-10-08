import createClient from 'openapi-fetch'
import type { components, paths } from './generated/schema'

/** Transport DTOs as described by the OpenAPI contract. */
export type Schemas = components['schemas']

/** Expected backend failure (RFC 9457 ProblemDetails with stable code and parameters). */
export class ApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly code: string,
    public readonly params: Record<string, unknown> = {},
  ) {
    super(code)
  }
}

let unauthorizedHandler: (() => void) | undefined

/** Called on every 401 (session expired); the session store redirects to the login. */
export function onUnauthorized(handler: () => void): void {
  unauthorizedHandler = handler
}

/** Typed API client. Components use stores/composables, never this client directly with fetch. */
export const api = createClient<paths>({
  baseUrl: globalThis.location?.origin ?? '',
  credentials: 'same-origin',
  // Resolved per call (not captured at start-up), so the transport can be replaced at the boundary in tests.
  fetch: (request: Request) => globalThis.fetch(request),
})

interface FetchResult<T> {
  data?: T
  error?: unknown
  response: Response
}

interface ProblemBody {
  code?: unknown
  params?: unknown
}

/** Unwraps an openapi-fetch result: returns the data or throws an {@link ApiError}. */
export async function unwrap<T>(request: Promise<FetchResult<T>>): Promise<T> {
  let result: FetchResult<T>
  try {
    result = await request
  } catch {
    throw new ApiError(0, 'network')
  }
  if (result.response.ok) {
    return result.data as T
  }
  if (result.response.status === 401) {
    unauthorizedHandler?.()
  }
  const problem = (result.error ?? {}) as ProblemBody
  const code = typeof problem.code === 'string' ? problem.code : `http.${result.response.status}`
  const params =
    typeof problem.params === 'object' && problem.params !== null ? (problem.params as Record<string, unknown>) : {}
  throw new ApiError(result.response.status, code, params)
}

/** URLs for `<img>` and `<video>` elements: built here, never assembled in components. */
export const mediaUrls = {
  recordingFile(id: string, file: Schemas['RecordingFile'], download = false): string {
    return `/api/recordings/${encodeURIComponent(id)}/files/${file}${download ? '?download=true' : ''}`
  },
  livePreview(kind: Schemas['PreviewKind']): string {
    return `/api/live/${kind}`
  },
}
