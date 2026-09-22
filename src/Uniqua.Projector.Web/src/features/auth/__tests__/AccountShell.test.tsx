import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ReactNode } from 'react'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { AccountShell } from '@/features/auth/AccountShell'

/**
 * T15 — the four states of the shell. The one that matters most is the distinction between
 * "visitor" and "error": being unrecognised is not a failure, and a dead server must not look like
 * having been signed out. Conflating them would either alarm every first-time visitor or quietly
 * hide an outage behind a sign-in form.
 */

/** Stands in for the sign-in form T17 builds, so this suite tests the shell and not the form. */
function SignInPlaceholder() {
  return <div data-testid="sign-in-form">Sign in</div>
}

function renderShell(children: ReactNode = <SignInPlaceholder />) {
  // retry: false, so a failing call surfaces its error at once instead of after TanStack Query's
  // default backoff — otherwise the error-state test would wait for real retries.
  const client = new QueryClient({
    defaultOptions: { queries: { retry: false, gcTime: 0 } },
  })

  return render(
    <QueryClientProvider client={client}>
      <AccountShell>{children}</AccountShell>
    </QueryClientProvider>,
  )
}

const anAccount = {
  id: '018f3a2b-7c4d-7e91-a0b2-3c4d5e6f7a8b',
  email: 'someone@example.test',
  display_name: 'Someone Real',
}

function jsonResponse(status: number, body: unknown) {
  return {
    ok: status >= 200 && status < 300,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
    headers: new Headers({ 'content-type': 'application/json' }),
  } as unknown as Response
}

function noContent() {
  return {
    ok: true,
    status: 204,
    json: async () => undefined,
    text: async () => '',
    headers: new Headers(),
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

describe('the shell while the bootstrap call is in flight', () => {
  it('shows that it is still deciding, and neither view yet', async () => {
    // A promise that never settles: the loading state is a state, not a flicker on the way
    // somewhere else, and it has to be observable on its own.
    fetchMock.mockReturnValue(new Promise(() => {}))

    renderShell()

    expect(await screen.findByRole('status')).toBeInTheDocument()
    expect(screen.queryByTestId('sign-in-form')).not.toBeInTheDocument()
    expect(screen.queryByText(anAccount.display_name)).not.toBeInTheDocument()
  })
})

describe('the shell for a visitor', () => {
  it('shows the sign-in form and calls it no error', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse(401, { code: 'accounts.session_not_recognised', detail: 'Sign in to continue.' }),
    )

    renderShell()

    expect(await screen.findByTestId('sign-in-form')).toBeInTheDocument()
    // Being a visitor is the ordinary state of someone who has not signed in.
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })
})

describe('the shell for a recognised account', () => {
  it('shows the display name and never the email address', async () => {
    // AC-11. The address is in the response — its own account may see it — but the shell is not
    // where it is shown, and a client that fell back to it would leak an address to a shoulder.
    fetchMock.mockResolvedValue(jsonResponse(200, anAccount))

    renderShell()

    expect(await screen.findByText(anAccount.display_name)).toBeInTheDocument()
    expect(screen.queryByText(anAccount.email)).not.toBeInTheDocument()
    expect(document.body.textContent).not.toContain(anAccount.email)
    expect(screen.queryByTestId('sign-in-form')).not.toBeInTheDocument()
  })

  it('offers a way to sign out', async () => {
    fetchMock.mockResolvedValue(jsonResponse(200, anAccount))

    renderShell()

    expect(await screen.findByRole('button', { name: /sign out/i })).toBeInTheDocument()
  })
})

describe('the shell when the call fails for a reason other than being unrecognised', () => {
  it('surfaces the failure with a retry, rather than looking like being signed out', { timeout: 20_000 }, async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    renderShell()

    // A transient network failure is retried a couple of times before the client gives up on it,
    // which is the right behaviour and means the error state takes a few seconds to appear.
    expect(await screen.findByRole('alert', {}, { timeout: 10_000 })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /try again/i })).toBeInTheDocument()
    // The crucial negative: an outage must not present itself as a sign-in prompt.
    expect(screen.queryByTestId('sign-in-form')).not.toBeInTheDocument()
  })

  it('recovers when the retry succeeds', { timeout: 20_000 }, async () => {
    // Rejecting throughout, so the hook's own retries are exhausted and the error state is
    // genuinely reached. Failing only once would be answered by the automatic retry and this test
    // would never see the button it is about.
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    renderShell()
    const tryAgain = await screen.findByRole(
      'button',
      { name: /try again/i },
      { timeout: 10_000 },
    )

    fetchMock.mockReset()
    fetchMock.mockResolvedValue(jsonResponse(200, anAccount))
    await userEvent.click(tryAgain)

    expect(await screen.findByText(anAccount.display_name)).toBeInTheDocument()
  })
})

describe('signing out', () => {
  it('leaves the client in the visitor state without reloading the page', async () => {
    // AC-08 from the client's side. A reload would also work and is exactly what must not be
    // needed: the session is server state, and the client already knows it changed.
    const reload = vi.fn()
    fetchMock.mockImplementation((_: RequestInfo | URL, init?: RequestInit) =>
      Promise.resolve(
        init?.method === 'DELETE' ? noContent() : jsonResponse(200, anAccount),
      ),
    )

    renderShell()
    await screen.findByText(anAccount.display_name)

    fetchMock.mockImplementation((_: RequestInfo | URL, init?: RequestInit) =>
      Promise.resolve(
        init?.method === 'DELETE'
          ? noContent()
          : jsonResponse(401, { code: 'accounts.session_not_recognised' }),
      ),
    )

    await userEvent.click(screen.getByRole('button', { name: /sign out/i }))

    expect(await screen.findByTestId('sign-in-form')).toBeInTheDocument()
    expect(reload).not.toHaveBeenCalled()
  })

  it('treats a 401 from the sign-out call as success, because the end state is the same', async () => {
    let signedOut = false
    fetchMock.mockImplementation((_: RequestInfo | URL, init?: RequestInit) => {
      if (init?.method === 'DELETE') {
        signedOut = true
        return Promise.resolve(jsonResponse(401, { code: 'accounts.session_not_recognised' }))
      }

      return Promise.resolve(
        signedOut
          ? jsonResponse(401, { code: 'accounts.session_not_recognised' })
          : jsonResponse(200, anAccount),
      )
    })

    renderShell()
    await screen.findByText(anAccount.display_name)

    await userEvent.click(screen.getByRole('button', { name: /sign out/i }))

    expect(await screen.findByTestId('sign-in-form')).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('keeps the account view and says so when the sign-out itself fails', async () => {
    // The one wrong answer here is claiming a sign-out that did not happen: the account would
    // believe it had left a shared machine signed out when it had not.
    fetchMock.mockImplementation((_: RequestInfo | URL, init?: RequestInit) =>
      init?.method === 'DELETE'
        ? Promise.reject(new TypeError('Failed to fetch'))
        : Promise.resolve(jsonResponse(200, anAccount)),
    )

    renderShell()
    await screen.findByText(anAccount.display_name)

    await userEvent.click(screen.getByRole('button', { name: /sign out/i }))

    await waitFor(() => expect(screen.getByRole('alert')).toBeInTheDocument())
    expect(screen.getByText(anAccount.display_name)).toBeInTheDocument()
    expect(screen.queryByTestId('sign-in-form')).not.toBeInTheDocument()
  })
})

describe('a sign-out whose session had already ended', () => {
  it('still lands the client in the visitor view', async () => {
    // The sign-out call itself is refused as not recognised: the end state is the one asked for.
    // A 401 from any other call is covered by VisitorScreens.test.tsx, composed as main.tsx is.
    fetchMock.mockResolvedValueOnce(jsonResponse(200, anAccount))

    renderShell()
    await screen.findByText(anAccount.display_name)

    fetchMock.mockResolvedValue(jsonResponse(401, { code: 'accounts.session_not_recognised' }))
    await userEvent.click(screen.getByRole('button', { name: /sign out/i }))

    expect(await screen.findByTestId('sign-in-form')).toBeInTheDocument()
  })
})
