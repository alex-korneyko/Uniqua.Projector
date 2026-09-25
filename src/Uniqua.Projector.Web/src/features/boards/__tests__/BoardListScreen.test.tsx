import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor, within } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes, useParams } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createQueryClient } from '@/api/queryClient'
import { AccountShell } from '@/features/auth/AccountShell'
import { BoardListScreen } from '@/features/boards/BoardListScreen'
import { keep } from '@/features/boards/draftStore'

/**
 * T19 / SCR-02 — the board list, in every state screens.md enumerates (AC-01..AC-04). Every test
 * drives the real `listMyBoards` transport through a stubbed `fetch`, the same way
 * `app/__tests__/routes.test.tsx` and the auth screens' suites do, so the session/board-list
 * interaction (a 401 ending the session, AC-28's kept text) is exercised as it actually happens
 * rather than through a mocked hook.
 */

const anAccount = {
  id: '018f3a2b-7c4d-7e91-a0b2-3c4d5e6f7a8b',
  email: 'someone@example.test',
  display_name: 'Someone Real',
}

const boardA = {
  id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a01',
  name: 'Alpha board',
  created_at: '2026-09-20T10:00:00Z',
  is_owner: true,
}

const boardB = {
  id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a02',
  name: 'Beta board',
  created_at: '2026-09-10T10:00:00Z',
  is_owner: false,
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

function problem(status: number, body: unknown) {
  return {
    ok: false,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
    headers: new Headers({ 'content-type': 'application/problem+json' }),
  } as unknown as Response
}

function boardListPage(items: unknown[]) {
  return { items, has_next: false, has_prev: false, next_cursor: null, prev_cursor: null }
}

// Reassigned per test so one fetch mock can answer both /accounts/me and /boards differently.
let boardsHandler: () => Promise<Response> = () => Promise.resolve(jsonResponse(200, boardListPage([])))

const fetchMock = vi.fn((input: RequestInfo | URL) => {
  const url = String(input)
  if (url.includes('/api/v1/accounts/me')) {
    return Promise.resolve(jsonResponse(200, anAccount))
  }
  if (url.includes('/api/v1/boards')) {
    return boardsHandler()
  }
  return Promise.resolve(problem(404, {}))
})

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockClear()
  boardsHandler = () => Promise.resolve(jsonResponse(200, boardListPage([])))
  document.cookie = 'XSRF-TOKEN=a-token'
  sessionStorage.clear()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function BoardIdSpy() {
  const { boardId } = useParams()
  return <div data-testid="opened-board">{boardId}</div>
}

function renderScreen(initialPath = '/') {
  const client = createQueryClient()
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initialPath]}>
        <AccountShell
          signedIn={
            <Routes>
              <Route path="/" element={<BoardListScreen />} />
              <Route path="/boards/:boardId" element={<BoardIdSpy />} />
            </Routes>
          }
        >
          <p>visitor</p>
        </AccountShell>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return client
}

describe('while the list is in flight', () => {
  it('shows a status line, and nothing else', async () => {
    boardsHandler = () => new Promise(() => {})

    renderScreen()

    // `findByRole('status')` would also match AccountShell's own "Checking your session…" status
    // line while the bootstrap call is still settling, so this queries the board-list wording
    // directly and then asserts it carries the status role.
    expect(await screen.findByText(/loading your boards/i)).toHaveAttribute('role', 'status')
  })
})

describe('the empty state (a first-time visitor with no boards)', () => {
  it('offers to create the first board', async () => {
    boardsHandler = () => Promise.resolve(jsonResponse(200, boardListPage([])))

    renderScreen()

    expect(await screen.findByText(/you have no boards yet/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /create board/i }))

    expect(await screen.findByRole('dialog', { name: /create board/i })).toBeInTheDocument()
  })
})

describe('the default state (AC-04)', () => {
  it('lists every board, newest first as the server sent it, marking the ones owned', async () => {
    boardsHandler = () => Promise.resolve(jsonResponse(200, boardListPage([boardA, boardB])))

    renderScreen()

    await screen.findByRole('heading', { name: /my boards/i })

    const alpha = screen.getByText('Alpha board')
    const beta = screen.getByText('Beta board')

    // AC-04: most recently created first, exactly as the server ordered them — never re-sorted.
    expect(
      alpha.compareDocumentPosition(beta) & Node.DOCUMENT_POSITION_FOLLOWING,
    ).toBeTruthy()

    // is_owner marks the owner, and only the owner.
    const alphaRow = alpha.closest('button') ?? alpha.closest('a')
    const betaRow = beta.closest('button') ?? beta.closest('a')
    expect(alphaRow).not.toBeNull()
    expect(betaRow).not.toBeNull()
    expect(alphaRow).toHaveTextContent(/owner/i)
    expect(betaRow).not.toHaveTextContent(/owner/i)
  })

  it('shows a board name literally, never as markup (edge case)', async () => {
    const dangerousBoard = {
      id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a09',
      name: '<script>alert(1)</script>',
      created_at: '2026-09-21T10:00:00Z',
      is_owner: true,
    }
    boardsHandler = () => Promise.resolve(jsonResponse(200, boardListPage([dangerousBoard])))

    renderScreen()

    expect(await screen.findByText('<script>alert(1)</script>')).toBeInTheDocument()
    expect(document.querySelector('script[src=""]')).toBeNull()
  })

  it('opens the board a row names (goes to /boards/:id)', async () => {
    boardsHandler = () => Promise.resolve(jsonResponse(200, boardListPage([boardA])))

    renderScreen()

    await userEvent.click(await screen.findByText('Alpha board'))

    expect(await screen.findByTestId('opened-board')).toHaveTextContent(boardA.id)
  })
})

describe('the error state (500, or no answer)', () => {
  it('offers a retry that asks again', async () => {
    let calls = 0
    boardsHandler = () => {
      calls += 1
      return calls === 1
        ? Promise.resolve(problem(500, { code: 'boards.request_invalid' }))
        : Promise.resolve(jsonResponse(200, boardListPage([boardA])))
    }

    renderScreen()

    expect(await screen.findByText(/we could not load your boards/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    await screen.findByText('Alpha board')
    expect(calls).toBe(2)
  })
})

describe('the session-ended state (401 on the read)', () => {
  it('is replaced by the sign-in form, and shows no board-list error of its own', async () => {
    boardsHandler = () =>
      Promise.resolve(
        problem(401, { code: 'accounts.session_not_recognised', detail: 'Sign in to continue.' }),
      )

    renderScreen()

    await waitFor(() => expect(screen.getByText('visitor')).toBeInTheDocument())
    expect(screen.queryByText(/we could not load your boards/i)).not.toBeInTheDocument()
  })
})

describe('the kept-text offer (AC-28, back on the list after SCR-01)', () => {
  it('offers to apply the kept board name again, reopening the dialog with it filled in', async () => {
    keep(anAccount.id, { item: 'board_name', fields: { name: 'Kept board name' } })
    boardsHandler = () => Promise.resolve(jsonResponse(200, boardListPage([boardA])))

    renderScreen()

    await screen.findByRole('heading', { name: /my boards/i })

    expect(screen.getByText(/kept board name/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /apply again/i }))

    const dialog = await screen.findByRole('dialog', { name: /create board/i })
    expect(within(dialog).getByLabelText(/board name/i)).toHaveValue('Kept board name')
  })
})

describe('after-delete (arriving from SCR-07 success, AC-20)', () => {
  it('shows the list without the deleted board, and no toast', async () => {
    boardsHandler = () => Promise.resolve(jsonResponse(200, boardListPage([boardA])))

    renderScreen()

    await screen.findByText('Alpha board')
    expect(screen.queryByText('Beta board')).not.toBeInTheDocument()
    // No success/deletion toast is invented on arrival.
    expect(screen.queryByText(/deleted|removed/i)).not.toBeInTheDocument()
  })
})
