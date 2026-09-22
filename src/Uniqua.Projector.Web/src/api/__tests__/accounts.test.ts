import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  ApiError,
  createSession,
  deleteCurrentSession,
  getCurrentAccount,
  registerAccount,
} from '@/api/accounts'

/**
 * T15 — the transport. Two of its responsibilities are invisible in any screen and would be
 * discovered only in production if they were wrong: every call has to send the session cookie, and
 * every state-changing call has to carry the antiforgery token the server issued. A client that
 * omitted either would appear to work locally and fail as soon as it met the real pipeline.
 */

const fetchMock = vi.fn()

function jsonResponse(status: number, body: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
    headers: new Headers({ 'content-type': 'application/json' }),
  } as unknown as Response
}

function initOf(call: number): RequestInit {
  return (fetchMock.mock.calls[call][1] ?? {}) as RequestInit
}

function headersOf(call: number): Headers {
  return new Headers(initOf(call).headers)
}

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
  document.cookie = 'XSRF-TOKEN=the-issued-token'
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('every call', () => {
  it('sends the session cookie', async () => {
    // The cookie is httpOnly, so the client cannot attach it by hand — it can only ask the browser
    // to include it. Omitting this makes every authenticated call anonymous.
    fetchMock.mockResolvedValue(jsonResponse(200, { id: 'x', email: 'a@b.test', display_name: 'A' }))

    await getCurrentAccount()

    expect(initOf(0).credentials).toBe('same-origin')
  })

  it('asks for the account by the path the contract names', async () => {
    fetchMock.mockResolvedValue(jsonResponse(200, { id: 'x', email: 'a@b.test', display_name: 'A' }))

    await getCurrentAccount()

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/accounts/me')
  })
})

describe('a state-changing call', () => {
  it('carries the antiforgery token the server issued', async () => {
    fetchMock.mockResolvedValue(jsonResponse(201, { id: 'x', email: 'a@b.test', display_name: 'A' }))

    await createSession({ email: 'a@b.test', password: 'a-long-enough-password' })

    expect(headersOf(0).get('X-XSRF-TOKEN')).toBe('the-issued-token')
  })

  it('is still attempted when no token has been issued yet', async () => {
    // Better to send the request and be refused by the server's own guard than to invent a
    // client-side refusal that the server would not have made.
    document.cookie = 'XSRF-TOKEN=; expires=Thu, 01 Jan 1970 00:00:00 GMT'
    fetchMock.mockResolvedValue(jsonResponse(403, { code: 'accounts.antiforgery_failed' }))

    await expect(
      createSession({ email: 'a@b.test', password: 'a-long-enough-password' }),
    ).rejects.toBeInstanceOf(ApiError)

    expect(fetchMock).toHaveBeenCalledOnce()
  })

  it('registers an account through the contract path and shape', async () => {
    fetchMock.mockResolvedValue(jsonResponse(201, { id: 'x', email: 'a@b.test', display_name: 'A' }))

    await registerAccount({
      email: 'a@b.test',
      password: 'a-long-enough-password',
      display_name: 'A',
    })

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/accounts')
    expect(initOf(0).method).toBe('POST')
    expect(JSON.parse(initOf(0).body as string)).toEqual({
      email: 'a@b.test',
      password: 'a-long-enough-password',
      display_name: 'A',
    })
  })

  it('signs out through the contract path and reads no body', async () => {
    fetchMock.mockResolvedValue({
      ok: true,
      status: 204,
      json: async () => {
        throw new Error('a 204 has no body to read')
      },
      text: async () => '',
      headers: new Headers(),
    } as unknown as Response)

    await deleteCurrentSession()

    expect(fetchMock.mock.calls[0][0]).toBe('/api/v1/sessions/current')
    expect(initOf(0).method).toBe('DELETE')
  })
})

describe('a refusal', () => {
  it('arrives as an error carrying the problem code the server sent', async () => {
    // The code is what a screen branches on. Branching on the wording would break the moment a
    // sentence is reworded, which the contract explicitly permits.
    fetchMock.mockResolvedValue(
      jsonResponse(409, {
        code: 'accounts.email_taken',
        detail: 'An email address identifies exactly one account.',
        status: 409,
      }),
    )

    const failure = await registerAccount({
      email: 'a@b.test',
      password: 'a-long-enough-password',
      display_name: 'A',
    }).catch((error: unknown) => error)

    expect(failure).toBeInstanceOf(ApiError)
    expect((failure as ApiError).code).toBe('accounts.email_taken')
    expect((failure as ApiError).status).toBe(409)
    expect((failure as ApiError).detail).toBe('An email address identifies exactly one account.')
  })

  it('carries the retry hint when the server sent one', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse(429, {
        code: 'accounts.registration_rate_limited',
        detail: 'Too many accounts have been created from here in the past minute.',
        retry_after_seconds: 37,
      }),
    )

    const failure = await registerAccount({
      email: 'a@b.test',
      password: 'a-long-enough-password',
      display_name: 'A',
    }).catch((error: unknown) => error)

    expect((failure as ApiError).retryAfterSeconds).toBe(37)
  })

  it('is still an error when the body is not a problem document at all', async () => {
    // A proxy or a gateway can answer instead of the application. The client must not assume the
    // body it hoped for and must not swallow the failure.
    fetchMock.mockResolvedValue({
      ok: false,
      status: 502,
      json: async () => {
        throw new Error('not json')
      },
      text: async () => '<html>Bad gateway</html>',
      headers: new Headers({ 'content-type': 'text/html' }),
    } as unknown as Response)

    const failure = await getCurrentAccount().catch((error: unknown) => error)

    expect(failure).toBeInstanceOf(ApiError)
    expect((failure as ApiError).status).toBe(502)
  })
})
