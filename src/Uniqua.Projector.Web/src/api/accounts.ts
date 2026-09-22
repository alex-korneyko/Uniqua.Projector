/**
 * The accounts transport, written against `docs/features/accounts-and-sessions/contracts/openapi.yaml`.
 *
 * Two of its responsibilities never show up in a screen and so are easy to get wrong until
 * production: every call has to let the browser attach the session cookie, and every state-changing
 * call has to echo the antiforgery token the server issued. Both live here, once, rather than at
 * each call site.
 */

/** The account as it is shown back to itself — the contract's `Account` schema. */
export interface Account {
  id: string
  /** Its own account may see its own address. AC-11 keeps it out of anything others see. */
  email: string
  display_name: string
}

/** The antiforgery cookie the server issues on every read, for the client to echo in a header. */
const antiforgeryCookieName = 'XSRF-TOKEN'
const antiforgeryHeaderName = 'X-XSRF-TOKEN'

/**
 * A refusal from the API, carrying the problem document's `code`.
 *
 * Screens branch on `code` and never on `detail`: the contract states that the code is "stable
 * across wording changes", so matching on the sentence would break the first time one is reworded.
 */
export class ApiError extends Error {
  readonly status: number
  readonly code: string | undefined
  readonly detail: string | undefined
  readonly retryAfterSeconds: number | undefined

  constructor(
    status: number,
    code: string | undefined,
    detail: string | undefined,
    retryAfterSeconds: number | undefined,
  ) {
    super(detail ?? `The request failed with status ${status}.`)
    this.name = 'ApiError'
    this.status = status
    this.code = code
    this.detail = detail
    this.retryAfterSeconds = retryAfterSeconds
  }

  /** Whether this refusal means "not signed in" — the one the client answers by showing the form. */
  get isNotRecognised(): boolean {
    return this.status === 401
  }
}

export function getCurrentAccount(): Promise<Account> {
  return request<Account>('/api/v1/accounts/me')
}

export interface RegisterAccountRequest {
  email: string
  password: string
  display_name: string
}

export function registerAccount(body: RegisterAccountRequest): Promise<Account> {
  return request<Account>('/api/v1/accounts', { method: 'POST', body })
}

export interface CreateSessionRequest {
  email: string
  password: string
}

export function createSession(body: CreateSessionRequest): Promise<Account> {
  return request<Account>('/api/v1/sessions', { method: 'POST', body })
}

export async function deleteCurrentSession(): Promise<void> {
  await request<void>('/api/v1/sessions/current', { method: 'DELETE', expectsBody: false })
}

interface RequestOptions {
  method?: string
  body?: unknown
  expectsBody?: boolean
}

async function request<TResult>(
  path: string,
  { method = 'GET', body, expectsBody = true }: RequestOptions = {},
): Promise<TResult> {
  const headers = new Headers()

  if (body !== undefined) {
    headers.set('content-type', 'application/json')
  }

  if (method !== 'GET' && method !== 'HEAD') {
    const token = readAntiforgeryToken()
    if (token !== undefined) {
      headers.set(antiforgeryHeaderName, token)
    }
    // With no token the request goes anyway. The server's own guard is the authority on whether it
    // is acceptable, and inventing a client-side refusal here would mean failing requests the
    // server would have allowed.
  }

  const response = await fetch(path, {
    method,
    headers,
    // The session cookie is httpOnly, so the client cannot attach it — it can only ask the browser
    // to. Without this every authenticated call is anonymous.
    credentials: 'same-origin',
    body: body === undefined ? undefined : JSON.stringify(body),
  })

  if (!response.ok) {
    throw await asApiError(response)
  }

  return expectsBody ? ((await response.json()) as TResult) : (undefined as TResult)
}

/**
 * Turns a failed response into an {@link ApiError}, whether or not it is the problem document the
 * contract promises — a proxy or a gateway can answer instead of the application, and a client
 * that assumed the shape would swallow that failure rather than report it.
 */
async function asApiError(response: Response): Promise<ApiError> {
  try {
    const problem = (await response.json()) as {
      code?: string
      detail?: string
      retry_after_seconds?: number
    }

    return new ApiError(
      response.status,
      problem.code,
      problem.detail,
      problem.retry_after_seconds,
    )
  } catch {
    return new ApiError(response.status, undefined, undefined, undefined)
  }
}

function readAntiforgeryToken(): string | undefined {
  const match = document.cookie
    .split(';')
    .map((entry) => entry.trim())
    .find((entry) => entry.startsWith(`${antiforgeryCookieName}=`))

  if (match === undefined) {
    return undefined
  }

  const value = decodeURIComponent(match.slice(antiforgeryCookieName.length + 1))
  return value.length > 0 ? value : undefined
}
