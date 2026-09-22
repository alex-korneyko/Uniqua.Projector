import { QueryClientProvider, type QueryClient } from '@tanstack/react-query'
import { act, render, screen, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/api/accounts'
import { createQueryClient, sessionQueryKey } from '@/api/queryClient'
import { AccountShell } from '@/features/auth/AccountShell'
import { VisitorScreens } from '@/features/auth/VisitorScreens'

/**
 * T30 — what someone is shown when they are not signed in, composed exactly as main.tsx does:
 * one query client, the real shell and the real visitor screens. Review 2026-09-22 R-08 and R-09
 * found both halves wrong and invisible to the suites that test the pieces apart.
 *
 * Every visitor is met by the sign-in form first — the owner's decision of 2026-09-22 (spec §8).
 * Most arrivals already have an account, and AC-07, AC-07b and AC-10 all end "presents the
 * sign-in form". A stranger from the public link reaches registration unaided through the link
 * inside the same card (AC-01), one click away.
 */

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

const notRecognised = () =>
  jsonResponse(401, {
    code: 'accounts.session_not_recognised',
    title: 'Not signed in',
    detail: 'Sign in to continue.',
  })

const fetchMock = vi.fn()
let client: QueryClient

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
})

afterEach(() => {
  vi.unstubAllGlobals()
  client.clear()
})

function renderApp() {
  client = createQueryClient()

  return render(
    <QueryClientProvider client={client}>
      <AccountShell>
        <VisitorScreens />
      </AccountShell>
    </QueryClientProvider>,
  )
}

const signInButton = () => screen.findByRole('button', { name: /^sign in$/i })

describe('a first-time arrival', () => {
  it('is met by the sign-in form, with the way to register inside the card', async () => {
    fetchMock.mockResolvedValue(notRecognised())

    renderApp()

    expect(await signInButton()).toBeInTheDocument()
    expect(screen.queryByText('Create an account')).not.toBeInTheDocument()
    // Inside <main>, not below a full-height one where it sits under the fold.
    expect(
      within(screen.getByRole('main')).getByRole('button', { name: /no account yet/i }),
    ).toBeInTheDocument()
  })

  it('reaches registration in one click, and can go back (AC-01)', async () => {
    fetchMock.mockResolvedValue(notRecognised())
    renderApp()

    // Landing on the sign-in form is not itself a "switch" — focus stays wherever the browser put
    // it, not forced onto the heading.
    await signInButton()
    expect(screen.getByRole('heading', { name: 'Sign in' })).not.toHaveFocus()

    await userEvent.click(await screen.findByRole('button', { name: /no account yet/i }))
    expect(await screen.findByText('Create an account')).toBeInTheDocument()
    // N-12: switching unmounted the focused toggle and left focus on <body>. Focus now follows
    // the new screen, landing on its heading.
    expect(screen.getByRole('heading', { name: 'Create an account' })).toHaveFocus()

    await userEvent.click(screen.getByRole('button', { name: /already have an account/i }))
    expect(await signInButton()).toBeInTheDocument()
    expect(screen.getByRole('heading', { name: 'Sign in' })).toHaveFocus()
  })
})

describe('someone whose session ended', () => {
  it('is shown the sign-in form when the session stops being recognised (AC-07, AC-10)', async () => {
    fetchMock.mockResolvedValue(jsonResponse(200, anAccount))
    renderApp()
    expect(await screen.findByText('Someone Real')).toBeInTheDocument()

    // The session expired or was revoked server-side; the next read says so.
    fetchMock.mockResolvedValue(notRecognised())
    await act(() => client.invalidateQueries({ queryKey: sessionQueryKey }))

    expect(await signInButton()).toBeInTheDocument()
    expect(screen.queryByText('Someone Real')).not.toBeInTheDocument()
    expect(screen.queryByText('Create an account')).not.toBeInTheDocument()
  })

  it('is shown the sign-in form after signing out', async () => {
    fetchMock.mockResolvedValue(jsonResponse(200, anAccount))
    renderApp()
    await screen.findByText('Someone Real')

    fetchMock.mockImplementation(async (_url: string, init?: RequestInit) =>
      init?.method === 'DELETE' ? noContent() : notRecognised(),
    )
    await userEvent.click(screen.getByRole('button', { name: /sign out/i }))

    expect(await signInButton()).toBeInTheDocument()
    expect(screen.queryByText('Create an account')).not.toBeInTheDocument()
  })

  it('is signed out by a 401 from any call, not only from the session check', async () => {
    fetchMock.mockResolvedValue(jsonResponse(200, anAccount))
    renderApp()
    await screen.findByText('Someone Real')

    fetchMock.mockResolvedValue(notRecognised())
    await act(async () => {
      await client
        .fetchQuery({
          queryKey: ['board', 'any'],
          queryFn: () =>
            Promise.reject(
              new ApiError(401, 'accounts.session_not_recognised', 'Not signed in', 'Sign in to continue.', undefined),
            ),
          retry: false,
        })
        .catch(() => undefined)
    })

    expect(await signInButton()).toBeInTheDocument()
    expect(screen.queryByText('Someone Real')).not.toBeInTheDocument()
  })
})

describe('a wrong password at sign-in', () => {
  it('is a refusal on the form, not a lost session', async () => {
    // The same status, 401, with a different code. Treating it as "the session ended" would
    // reset the form under the visitor's hands.
    fetchMock.mockResolvedValue(notRecognised())
    renderApp()
    await signInButton()

    fetchMock.mockResolvedValue(
      jsonResponse(401, {
        code: 'accounts.credentials_invalid',
        title: 'The address or the password is incorrect',
        detail: 'The address or the password is incorrect.',
      }),
    )
    await userEvent.type(screen.getByLabelText(/email address/i), 'someone@example.test')
    await userEvent.type(screen.getByLabelText(/password/i), 'not-the-password')
    await userEvent.click(await signInButton())

    expect(await screen.findByRole('alert')).toHaveTextContent('The address or the password is incorrect.')
    expect(screen.getByLabelText(/email address/i)).toHaveValue('someone@example.test')
  })
})
