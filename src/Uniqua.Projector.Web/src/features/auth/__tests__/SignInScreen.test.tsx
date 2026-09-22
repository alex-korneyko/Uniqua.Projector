import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { sessionQueryKey } from '@/api/queryClient'

import { SignInScreen } from '@/features/auth/SignInScreen'

/**
 * T17 — the sign-in screen. Its whole discipline is restraint.
 *
 * The server goes to real trouble to make a wrong password and an unregistered address
 * indistinguishable — one code, one sentence, a comparable wait. A client that validated the
 * address shape before submitting, or that worded the two cases differently, would hand back the
 * distinction the server just spent a full password verification hiding. So the interesting tests
 * here assert that the screen does *less* than it might.
 */

let client: QueryClient

function renderScreen() {
  client = new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })

  return render(
    <QueryClientProvider client={client}>
      <SignInScreen />
    </QueryClientProvider>,
  )
}

/** The one refusal, exactly as the contract states it. */
const credentialsInvalid = {
  code: 'accounts.credentials_invalid',
  detail: 'The address or the password is incorrect.',
  status: 401,
}

function problem(status: number, body: unknown) {
  return {
    ok: false,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
    headers: new Headers({ 'content-type': 'application/problem+json' }),
  } as unknown as Response
}

function created() {
  return {
    ok: true,
    status: 201,
    json: async () => ({
      id: '018f3a2b-7c4d-7e91-a0b2-3c4d5e6f7a8b',
      email: 'someone@example.test',
      display_name: 'Someone Real',
    }),
    text: async () => '',
    headers: new Headers({ 'content-type': 'application/json' }),
  } as unknown as Response
}

const fetchMock = vi.fn()

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
  document.cookie = 'XSRF-TOKEN=a-token'
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function emailField() {
  return screen.getByLabelText(/email/i)
}

function passwordField() {
  return screen.getByLabelText(/password/i)
}

/** Matches the button in both its labels, since it says "Signing in…" while in flight. */
function submitButton() {
  return screen.getByRole('button', { name: /sign(ing)? in/i })
}

async function submit(email: string, password: string) {
  if (email.length > 0) {
    await userEvent.type(emailField(), email)
  }
  if (password.length > 0) {
    await userEvent.type(passwordField(), password)
  }

  await userEvent.click(submitButton())
}

// ---- 1. the default state -----------------------------------------------------------------------

describe('the default state', () => {
  it('offers an address and a password, and no error', () => {
    renderScreen()

    expect(emailField()).toHaveValue('')
    expect(passwordField()).toHaveValue('')
    expect(passwordField()).toHaveAttribute('type', 'password')
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

// ---- 2. the submitting state --------------------------------------------------------------------

describe('while the submission is in flight', () => {
  it('cannot be submitted twice', async () => {
    fetchMock.mockReturnValue(new Promise(() => {}))

    renderScreen()
    await submit('someone@example.test', 'a-long-enough-password')

    await waitFor(() => expect(submitButton()).toBeDisabled())
    await userEvent.click(submitButton())

    expect(fetchMock).toHaveBeenCalledOnce()
  })
})

// ---- 3. the one refusal (AC-05, AC-05b) ---------------------------------------------------------

describe('the refusal', () => {
  it('reads identically for a wrong password and for an unregistered address', async () => {
    // The server returns one code and one sentence for both. This test exists to make sure the
    // client does not undo that by branching on anything.
    fetchMock.mockResolvedValue(problem(401, credentialsInvalid))

    const wrongPassword = renderScreen()
    await submit('registered@example.test', 'not-the-password')
    const first = (await screen.findByRole('alert')).textContent
    wrongPassword.unmount()

    renderScreen()
    await submit('never-registered@example.test', 'any-password-at-all')
    const second = (await screen.findByRole('alert')).textContent

    expect(second).toBe(first)
    expect(first).toBe('The address or the password is incorrect.')
  })

  it('highlights neither field, because highlighting one would answer the question', async () => {
    fetchMock.mockResolvedValue(problem(401, credentialsInvalid))

    renderScreen()
    await submit('someone@example.test', 'not-the-password')
    await screen.findByRole('alert')

    // aria-invalid on the address would say "the address was the problem", which is precisely
    // what AC-05 withholds.
    expect(emailField()).not.toHaveAttribute('aria-invalid', 'true')
    expect(passwordField()).not.toHaveAttribute('aria-invalid', 'true')
  })

  it('keeps what was typed so a mistyped password can be corrected', async () => {
    fetchMock.mockResolvedValue(problem(401, credentialsInvalid))

    renderScreen()
    await submit('someone@example.test', 'not-the-password')
    await screen.findByRole('alert')

    expect(emailField()).toHaveValue('someone@example.test')
    expect(passwordField()).toHaveValue('not-the-password')
  })

  it('reads the same however long the server took to answer', async () => {
    // AC-12's delay is applied before this same refusal and is not signalled at all. A client that
    // said "too many attempts" on a slow response would announce the throttle the server hides.
    fetchMock.mockImplementation(
      () =>
        new Promise((resolve) =>
          setTimeout(() => resolve(problem(401, credentialsInvalid)), 50),
        ),
    )

    renderScreen()
    await submit('someone@example.test', 'not-the-password')

    const message = await screen.findByRole('alert')
    expect(message).toHaveTextContent('The address or the password is incorrect.')
    expect(message.textContent).not.toMatch(/too many|locked|attempts|wait|delay/i)
  })
})

// ---- The restraint: no pre-check may stand between a visitor and the server ---------------------

describe('what the screen deliberately does not check', () => {
  it('submits an address that could not possibly be registered', async () => {
    // A client-side address check would refuse instantly what the server refuses slowly, and the
    // difference in speed is itself an answer to "is this address registered?".
    fetchMock.mockResolvedValue(problem(401, credentialsInvalid))

    renderScreen()
    await submit('not-an-address', 'a-long-enough-password')

    await waitFor(() => expect(fetchMock).toHaveBeenCalledOnce())
    expect(await screen.findByRole('alert')).toHaveTextContent(
      'The address or the password is incorrect.',
    )
  })

  it('submits a password far shorter than any account could have', async () => {
    // Refusing it here would reveal that the rejection was about length rather than about the
    // credential, and would refuse a request the server was willing to consider.
    fetchMock.mockResolvedValue(problem(401, credentialsInvalid))

    renderScreen()
    await submit('someone@example.test', 'x')

    await waitFor(() => expect(fetchMock).toHaveBeenCalledOnce())
  })

  it('carries no minimum length or address pattern on its fields', async () => {
    renderScreen()

    expect(passwordField()).not.toHaveAttribute('minlength')
    expect(passwordField()).not.toHaveAttribute('pattern')
    expect(emailField()).not.toHaveAttribute('pattern')
  })
})

// ---- 4. signed in (AC-04) -----------------------------------------------------------------------

describe('when the credentials are accepted', () => {
  it('leaves the client signed in with no refusal showing', async () => {
    fetchMock.mockImplementation((_: RequestInfo | URL, init?: RequestInit) =>
      Promise.resolve(init?.method === 'POST' ? created() : problem(401, credentialsInvalid)),
    )

    renderScreen()
    await submit('someone@example.test', 'a-long-enough-password')

    await waitFor(() => expect(screen.queryByRole('alert')).not.toBeInTheDocument())
    expect(submitButton()).toBeEnabled()
  })

  it('knows who is signed in from the 201 itself, without waiting for the session check', async () => {
    // Review 2026-09-22 R-20: until /me answers, the session would otherwise still read "visitor".
    fetchMock.mockImplementation((_: RequestInfo | URL, init?: RequestInit) =>
      init?.method === 'POST' ? Promise.resolve(created()) : new Promise(() => {}),
    )

    renderScreen()
    // In the app the shell always observes the session; kept here as it would keep it, so this
    // suite's gcTime: 0 does not collect what the screen wrote before it can be read.
    client.setQueryDefaults(sessionQueryKey, { gcTime: Infinity })
    await submit('someone@example.test', 'a-long-enough-password')

    await waitFor(() =>
      expect(client.getQueryData(sessionQueryKey)).toMatchObject({ display_name: 'Someone Real' }),
    )
  })
})

// ---- 5. the sign-in rate limit (AC-12) -----------------------------------------------------------

describe('when sign-ins from this source are rate limited', () => {
  const signInRateLimited = {
    code: 'accounts.sign_in_rate_limited',
    title: 'Sign-in is temporarily limited',
    detail: 'Too many failed sign-in attempts have come from here recently.',
    status: 429,
    retry_after_seconds: 412,
  }

  it('shows the statement and the reason, names the wait once, and keeps the form usable', async () => {
    fetchMock.mockResolvedValue(problem(429, signInRateLimited))

    renderScreen()
    await submit('someone@example.test', 'not-the-password')

    const message = await screen.findByRole('alert')
    expect(message).toHaveTextContent('Sign-in is temporarily limited.')
    expect(message).toHaveTextContent('Too many failed sign-in attempts have come from here recently.')
    // The wait is named once, not zero times and not twice.
    expect(message.textContent?.match(/412 seconds/g)).toHaveLength(1)

    // Never the credentials wording: a rate limit is not a claim about what was typed.
    expect(message.textContent).not.toContain('The address or the password is incorrect.')

    expect(submitButton()).toBeEnabled()
    expect(emailField()).not.toBeDisabled()
    expect(passwordField()).not.toBeDisabled()
  })

  it('does not end an existing session', async () => {
    fetchMock.mockResolvedValue(problem(429, signInRateLimited))

    renderScreen()
    client.setQueryDefaults(sessionQueryKey, { gcTime: Infinity })
    client.setQueryData(sessionQueryKey, { id: 'already-signed-in', display_name: 'Someone Real' })
    await submit('someone@example.test', 'not-the-password')

    await screen.findByRole('alert')
    expect(client.getQueryData(sessionQueryKey)).toMatchObject({ id: 'already-signed-in' })
  })
})

// ---- Failures that are not refusals -------------------------------------------------------------

describe('when the server cannot be reached', () => {
  it('says so, distinctly from a refusal, and keeps the form usable', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    renderScreen()
    await submit('someone@example.test', 'a-long-enough-password')

    const message = await screen.findByRole('alert')
    // Not the credentials wording: a visitor whose network died has not got their password wrong.
    expect(message.textContent).not.toContain('The address or the password is incorrect.')
    expect(submitButton()).toBeEnabled()
    expect(passwordField()).toHaveValue('a-long-enough-password')
  })
})
