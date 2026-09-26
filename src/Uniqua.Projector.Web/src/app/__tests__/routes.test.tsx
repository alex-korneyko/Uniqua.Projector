import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, useLocation } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { isInAppPath } from '@/app/routes'
import { AccountShell } from '@/features/auth/AccountShell'
import { VisitorScreens } from '@/features/auth/VisitorScreens'

/**
 * T18 / AC-27 / ADR 0012 — a board has an address of its own, a visitor with no session is shown
 * the ordinary sign-in form at it (screens.md SCR-01 `from-board-link`), and signing in returns to
 * that address only when it is a path inside the application (the `returnTo` guard, sad.md §8).
 *
 * `isInAppPath` is the guard `routes.tsx` owns (ADR 0012 Consequences: "ours to write and test
 * regardless of router"). The rest of this file exercises it through the shell, because that is
 * where a visitor actually experiences it.
 */

describe('isInAppPath', () => {
  it.each([
    ['/boards/018f3a2b-7c4d-7e91-a0b2-3c4d5e6f7a8b', true],
    ['/', true],
    ['//evil.example', false],
    ['https://evil.example', false],
    ['/\\evil', false],
    ['', false],
    ['boards/018f3a2b', false],
    // A browser strips a tab, turning this into `//evil` — another host.
    ['/\t/evil', false],
    ['/\u0000x', false],
    // Percent-encoded slashes are never decoded into the path's first segment: it stays in-app.
    ['/%2F%2Fevil', true],
  ])('isInAppPath(%j) is %p', (path, expected) => {
    expect(isInAppPath(path)).toBe(expected)
  })
})

function LocationSpy() {
  const location = useLocation()
  return <div data-testid="current-path">{location.pathname}</div>
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

const anAccount = {
  id: '018f3a2b-7c4d-7e91-a0b2-3c4d5e6f7a8b',
  email: 'someone@example.test',
  display_name: 'Someone Real',
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

function testClient() {
  return new QueryClient({ defaultOptions: { queries: { retry: false, gcTime: 0 } } })
}

function renderApp(initialPath: string, client: QueryClient) {
  return render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initialPath]}>
        <LocationSpy />
        <AccountShell>
          <VisitorScreens />
        </AccountShell>
      </MemoryRouter>
    </QueryClientProvider>,
  )
}

describe('a visitor who follows a board link (AC-27)', () => {
  it('sees the unchanged sign-in form and makes no request naming the board', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse(401, { code: 'accounts.session_not_recognised', detail: 'Sign in to continue.' }),
    )

    const boardPath = '/boards/018f3a2b-7c4d-7e91-a0b2-3c4d5e6f7a8b'
    renderApp(boardPath, testClient())

    // Same sign-in form as the default state — a real board and one that never existed are
    // answered identically, so nothing here may depend on which this is.
    expect(await screen.findByRole('heading', { name: /sign in/i })).toBeInTheDocument()
    expect(screen.getByTestId('current-path')).toHaveTextContent(boardPath)

    const namedTheBoard = fetchMock.mock.calls.some(([input]) =>
      String(input).includes('/api/v1/boards'),
    )
    expect(namedTheBoard).toBe(false)
  })

  it('returns to the link address after signing in, when it is a path inside the application', async () => {
    const boardPath = '/boards/018f3a2b-7c4d-7e91-a0b2-3c4d5e6f7a8b'
    let signedIn = false

    fetchMock.mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)

      if (init?.method === 'POST' && url.includes('/api/v1/sessions')) {
        signedIn = true
        return Promise.resolve(jsonResponse(201, anAccount))
      }

      return Promise.resolve(
        signedIn
          ? jsonResponse(200, anAccount)
          : jsonResponse(401, { code: 'accounts.session_not_recognised' }),
      )
    })

    renderApp(boardPath, testClient())

    await screen.findByRole('heading', { name: /sign in/i })
    await userEvent.type(screen.getByLabelText(/email address/i), 'someone@example.test')
    await userEvent.type(screen.getByLabelText(/password/i), 'whatever-they-typed')
    await userEvent.click(screen.getByRole('button', { name: /^sign in$/i }))

    await waitFor(() =>
      expect(screen.getByTestId('current-path')).toHaveTextContent(boardPath),
    )
  })

  it('lands on the board list instead of following an out-of-app returnTo', async () => {
    // //evil.example parses, in a browser and in React Router's MemoryRouter alike, as a path — so
    // this is the one case the guard alone stands between a link and an open redirect.
    const maliciousPath = '//evil.example'
    let signedIn = false

    fetchMock.mockImplementation((input: RequestInfo | URL, init?: RequestInit) => {
      const url = String(input)

      if (init?.method === 'POST' && url.includes('/api/v1/sessions')) {
        signedIn = true
        return Promise.resolve(jsonResponse(201, anAccount))
      }

      return Promise.resolve(
        signedIn
          ? jsonResponse(200, anAccount)
          : jsonResponse(401, { code: 'accounts.session_not_recognised' }),
      )
    })

    renderApp(maliciousPath, testClient())

    await screen.findByRole('heading', { name: /sign in/i })
    await userEvent.type(screen.getByLabelText(/email address/i), 'someone@example.test')
    await userEvent.type(screen.getByLabelText(/password/i), 'whatever-they-typed')
    await userEvent.click(screen.getByRole('button', { name: /^sign in$/i }))

    // Exactly the board list: `toHaveTextContent('/')` would also match `//evil.example` itself.
    await waitFor(() => expect(screen.getByTestId('current-path').textContent).toBe('/'))
  })
})
