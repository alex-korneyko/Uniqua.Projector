import { QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { createQueryClient } from '@/api/queryClient'
import { AccountShell } from '@/features/auth/AccountShell'
import { BoardListScreen } from '@/features/boards/BoardListScreen'
import { BoardScreen } from '@/features/boards/BoardScreen'
import { keep } from '@/features/boards/draftStore'

/**
 * T20 / SCR-04 + SCR-08. Every test drives the real `openBoard` / `renameBoard` / `addCard`
 * transports through a stubbed `fetch`, the same way `BoardListScreen.test.tsx` does, so the
 * board-screen/session interaction (a 401 ending the session, AC-18b's re-read, AC-28's kept text)
 * is exercised as it actually happens rather than through a mocked hook.
 */

const anAccount = {
  id: '018f3a2b-7c4d-7e91-a0b2-3c4d5e6f7a8b',
  email: 'someone@example.test',
  display_name: 'Someone Real',
}

const boardId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a01'
const columnId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7c01'

function aColumn(overrides: Partial<Record<string, unknown>> = {}) {
  return { id: columnId, name: 'To do', position: 0, name_version: 1, ...overrides }
}

function aCard(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7d01',
    column_id: columnId,
    position: 0,
    title: 'First card',
    content_version: 1,
    ...overrides,
  }
}

function aBoard(overrides: Partial<Record<string, unknown>> = {}) {
  return {
    id: boardId,
    name: 'Launch plan',
    is_owner: false,
    column_layout_version: 1,
    columns: [aColumn()],
    cards: [],
    ...overrides,
  }
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

const notAvailable = () =>
  problem(404, {
    code: 'boards.not_available',
    title: 'Board not available',
    detail: 'This board does not exist, or you are not a member of it.',
  })

// Reassigned per test: a queue of answers for successive GET /boards/:id calls, and one handler
// for whatever change the test drives (rename or add-card).
let openHandler: () => Promise<Response> = () => Promise.resolve(jsonResponse(200, aBoard()))
let changeHandler: (input: string, init: RequestInit | undefined) => Promise<Response> | undefined =
  () => undefined

const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  const method = init?.method ?? 'GET'

  if (url.includes('/api/v1/accounts/me')) {
    return Promise.resolve(jsonResponse(200, anAccount))
  }

  if (url.includes(`/api/v1/boards/${boardId}`) && method === 'GET') {
    return openHandler()
  }

  const changed = changeHandler(url, init)
  if (changed !== undefined) {
    return changed
  }

  return Promise.resolve(problem(404, {}))
})

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockClear()
  openHandler = () => Promise.resolve(jsonResponse(200, aBoard()))
  changeHandler = () => undefined
  document.cookie = 'XSRF-TOKEN=a-token'
  sessionStorage.clear()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function renderScreen(initialPath = `/boards/${boardId}`) {
  const client = createQueryClient()
  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[initialPath]}>
        <AccountShell
          signedIn={
            <Routes>
              <Route path="/" element={<BoardListScreen />} />
              <Route path="/boards/:boardId" element={<BoardScreen />} />
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

// ---- loading / error / not-available -------------------------------------------------------------

describe('while the board is in flight (no seeded cache)', () => {
  it('shows a status line, and nothing else', async () => {
    openHandler = () => new Promise(() => {})

    renderScreen()

    expect(await screen.findByText(/loading the board/i)).toHaveAttribute('role', 'status')
  })
})

describe('the error state (500, or no answer)', () => {
  it('offers a retry that asks again', async () => {
    let calls = 0
    openHandler = () => {
      calls += 1
      return calls === 1
        ? Promise.resolve(problem(500, { code: 'boards.request_invalid' }))
        : Promise.resolve(jsonResponse(200, aBoard()))
    }

    renderScreen()

    expect(await screen.findByText(/we could not load this board/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /try again/i }))

    await screen.findByRole('heading', { name: /launch plan/i })
    expect(calls).toBe(2)
  })
})

describe('SCR-08 — not available', () => {
  it.each([
    ['a non-member', notAvailable],
    ['a board that was deleted', notAvailable],
    ['a board that never existed', notAvailable],
  ])('renders identical DOM and document.title for %s', async (_label, handler) => {
    openHandler = () => Promise.resolve(handler())

    renderScreen()

    expect(await screen.findByRole('heading', { name: /board not available/i })).toBeInTheDocument()
    expect(
      screen.getByText(/this board does not exist, or you are not a member of it/i),
    ).toBeInTheDocument()

    // No name, no id and no hint of which case applies.
    expect(screen.queryByText('Launch plan')).not.toBeInTheDocument()
    expect(document.body.textContent).not.toContain(boardId)
    expect(document.title).not.toMatch(/launch plan/i)

    const backButton = screen.getByRole('button', { name: /back to my boards/i })
    expect(backButton).toBeInTheDocument()
  })

  it('goes back to the board list from the "Back to my boards" button', async () => {
    openHandler = () => Promise.resolve(notAvailable())

    renderScreen()

    await userEvent.click(await screen.findByRole('button', { name: /back to my boards/i }))

    await waitFor(() =>
      expect(fetchMock.mock.calls.some(([input]) => String(input).endsWith('/api/v1/boards'))).toBe(
        true,
      ),
    )
  })
})

// ---- default states: member vs owner --------------------------------------------------------------

describe('the default state, a member (is_owner: false)', () => {
  it('shows the back button and the board name, with no rename or delete controls', async () => {
    openHandler = () =>
      Promise.resolve(
        jsonResponse(
          200,
          aBoard({ is_owner: false, columns: [aColumn()], cards: [aCard()] }),
        ),
      )

    renderScreen()

    expect(await screen.findByRole('button', { name: /my boards/i })).toBeInTheDocument()
    expect(await screen.findByRole('heading', { name: /launch plan/i, level: 1 })).toBeInTheDocument()

    expect(screen.queryByRole('button', { name: /rename board/i })).not.toBeInTheDocument()
    expect(screen.queryByRole('button', { name: /delete board/i })).not.toBeInTheDocument()

    // The card tile is shown, ordered by position, its title as literal text.
    const tile = screen.getByRole('button', { name: 'First card' })
    expect(tile).toBeInTheDocument()
  })

  it('shows a card title literally, never as markup (edge case)', async () => {
    openHandler = () =>
      Promise.resolve(
        jsonResponse(
          200,
          aBoard({ cards: [aCard({ title: '<script>alert(1)</script>' })] }),
        ),
      )

    renderScreen()

    expect(await screen.findByText('<script>alert(1)</script>')).toBeInTheDocument()
    expect(document.querySelector('script[src=""]')).toBeNull()
  })
})

describe('the default state, the owner (is_owner: true)', () => {
  it('additionally shows Rename board and Delete board', async () => {
    openHandler = () => Promise.resolve(jsonResponse(200, aBoard({ is_owner: true })))

    renderScreen()

    expect(await screen.findByRole('button', { name: /rename board/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /delete board/i })).toBeInTheDocument()
  })
})

// ---- board rename (owner) -------------------------------------------------------------------------

describe('board rename', () => {
  function withRenameHandler(answer: () => Response) {
    changeHandler = (url, init) => {
      if (url.includes(`/api/v1/boards/${boardId}`) && init?.method === 'PATCH') {
        return Promise.resolve(answer())
      }
      return undefined
    }
  }

  async function openEditor() {
    renderScreen()
    await userEvent.click(await screen.findByRole('button', { name: /rename board/i }))
    return screen.getByLabelText(/board name/i)
  }

  beforeEach(() => {
    openHandler = () => Promise.resolve(jsonResponse(200, aBoard({ is_owner: true })))
  })

  it('editing: shows the input with a 1-100 hint, prefilled with the current name', async () => {
    const input = await openEditor()
    expect(input).toHaveValue('Launch plan')
    expect(screen.getByText(/between 1 and 100 characters/i)).toBeInTheDocument()
  })

  it('pending: shows "Saving…" while the request is in flight', async () => {
    withRenameHandler(() => jsonResponse(200, { id: boardId, name: 'New name' }))
    changeHandler = (url, init) => {
      if (url.includes(`/api/v1/boards/${boardId}`) && init?.method === 'PATCH') {
        return new Promise(() => {})
      }
      return undefined
    }

    const input = await openEditor()
    await userEvent.clear(input)
    await userEvent.type(input, 'New name')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    expect(await screen.findByText(/saving/i)).toBeInTheDocument()
  })

  it('validation 400 board_name_invalid: shows the refusal line and keeps what was typed', async () => {
    withRenameHandler(() =>
      problem(400, {
        code: 'boards.board_name_invalid',
        title: 'The board name is not usable.',
        detail: 'A board name must be between 1 and 100 characters.',
      }),
    )

    const input = await openEditor()
    await userEvent.clear(input)
    await userEvent.type(input, 'Bad name')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    expect(await screen.findByText(/board name must be between 1 and 100 characters/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/board name/i)).toHaveValue('Bad name')
  })

  it('owner-only 403: shows the refusal, re-reads the board, and the controls disappear', async () => {
    withRenameHandler(() =>
      problem(403, {
        code: 'boards.owner_only',
        title: 'Only the board owner may do that.',
        detail: 'Only the board owner may rename or delete a board.',
      }),
    )
    let reads = 0
    openHandler = () => {
      reads += 1
      return Promise.resolve(jsonResponse(200, aBoard({ is_owner: reads === 1 })))
    }

    const input = await openEditor()
    await userEvent.type(input, ' more')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    expect(
      await screen.findByText(/only the board owner may rename or delete a board/i),
    ).toBeInTheDocument()

    await waitFor(() =>
      expect(screen.queryByRole('button', { name: /rename board/i })).not.toBeInTheDocument(),
    )
  })

  it('success 200: patches the title and closes the editor', async () => {
    withRenameHandler(() => jsonResponse(200, { id: boardId, name: 'Renamed plan' }))

    const input = await openEditor()
    await userEvent.clear(input)
    await userEvent.type(input, 'Renamed plan')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    expect(await screen.findByRole('heading', { name: /renamed plan/i, level: 1 })).toBeInTheDocument()
    expect(screen.queryByLabelText(/board name/i)).not.toBeInTheDocument()
  })
})

// ---- add card ---------------------------------------------------------------------------------------

describe('add card', () => {
  function withAddCardHandler(answer: () => Response) {
    changeHandler = (url, init) => {
      if (url.includes(`/api/v1/boards/${boardId}/cards`) && init?.method === 'POST') {
        return Promise.resolve(answer())
      }
      return undefined
    }
  }

  async function openForm() {
    renderScreen()
    await screen.findByRole('heading', { name: /launch plan/i })
    await userEvent.click(screen.getByRole('button', { name: /add card/i }))
    return {
      title: screen.getByLabelText(/^title$/i),
      description: screen.getByLabelText(/description/i),
    }
  }

  beforeEach(() => {
    openHandler = () => Promise.resolve(jsonResponse(200, aBoard({ is_owner: false, columns: [aColumn()] })))
  })

  it('closed: shows a "+ Add card" affordance and no form', async () => {
    renderScreen()
    await screen.findByRole('heading', { name: /launch plan/i })

    expect(screen.getByRole('button', { name: /\+\s*add card/i })).toBeInTheDocument()
    expect(screen.queryByLabelText(/^title$/i)).not.toBeInTheDocument()
  })

  it('editing: shows Title (1-150) and Description (optional, up to 10,000) with their hints', async () => {
    const { title, description } = await openForm()

    expect(title).toBeInTheDocument()
    expect(description).toBeInTheDocument()
    expect(screen.getByText(/between 1 and 150 characters/i)).toBeInTheDocument()
    expect(screen.getByText(/at most 10,000 characters/i)).toBeInTheDocument()
  })

  it('pending: disables the submit control while the request is in flight', async () => {
    changeHandler = (url, init) => {
      if (url.includes(`/api/v1/boards/${boardId}/cards`) && init?.method === 'POST') {
        return new Promise(() => {})
      }
      return undefined
    }

    const { title } = await openForm()
    await userEvent.type(title, 'A new card')
    await userEvent.click(screen.getByRole('button', { name: /^add$/i }))

    expect(screen.getByRole('button', { name: /^add$/i })).toBeDisabled()
  })

  it('validation 400 card_title_invalid: shows the refusal under the title field, keeping both fields', async () => {
    withAddCardHandler(() =>
      problem(400, {
        code: 'boards.card_title_invalid',
        title: 'The card title is not usable.',
        detail: 'A card title must be between 1 and 150 characters.',
      }),
    )

    const { title, description } = await openForm()
    await userEvent.type(title, 'x'.repeat(1))
    await userEvent.type(description, 'Kept description')
    await userEvent.click(screen.getByRole('button', { name: /^add$/i }))

    expect(
      await screen.findByText(/card title must be between 1 and 150 characters/i),
    ).toBeInTheDocument()
    expect(screen.getByLabelText(/description/i)).toHaveValue('Kept description')
  })

  it('validation 400 card_description_invalid: shows the refusal under the description field, keeping both', async () => {
    withAddCardHandler(() =>
      problem(400, {
        code: 'boards.card_description_invalid',
        title: 'The card description is not usable.',
        detail: 'A card description must be at most 10,000 characters.',
      }),
    )

    const { title, description } = await openForm()
    await userEvent.type(title, 'A title')
    await userEvent.type(description, 'Too long')
    await userEvent.click(screen.getByRole('button', { name: /^add$/i }))

    expect(
      await screen.findByText(/card description must be at most 10,000 characters/i),
    ).toBeInTheDocument()
    expect(screen.getByLabelText(/^title$/i)).toHaveValue('A title')
  })

  it('limit 409 card_limit_reached: refuses and tells the 1,000-card limit (AC-15)', async () => {
    withAddCardHandler(() =>
      problem(409, {
        code: 'boards.card_limit_reached',
        title: 'This board is full.',
        detail: 'A board can hold at most 1,000 cards.',
      }),
    )

    const { title } = await openForm()
    await userEvent.type(title, 'One more card')
    await userEvent.click(screen.getByRole('button', { name: /^add$/i }))

    expect(await screen.findByText(/a board can hold at most 1,000 cards/i)).toBeInTheDocument()
  })

  it('gone (AC-18b): 404 on add, board re-read 200 — refreshes and offers the kept text', async () => {
    let reReadCount = 0
    changeHandler = (url, init) => {
      if (url.includes(`/api/v1/boards/${boardId}/cards`) && init?.method === 'POST') {
        return Promise.resolve(
          problem(404, { code: 'boards.not_available', title: 'That column no longer exists.' }),
        )
      }
      return undefined
    }
    openHandler = () => {
      reReadCount += 1
      return Promise.resolve(
        jsonResponse(200, reReadCount === 1 ? aBoard() : aBoard({ columns: [] })),
      )
    }

    const { title, description } = await openForm()
    await userEvent.type(title, 'Orphaned title')
    await userEvent.type(description, 'Orphaned description')
    await userEvent.click(screen.getByRole('button', { name: /^add$/i }))

    expect(await screen.findByText(/that column no longer exists/i)).toBeInTheDocument()
    expect(screen.getByText('Orphaned title')).toBeInTheDocument()
    expect(screen.getByText('Orphaned description')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /dismiss/i })).toBeInTheDocument()
    expect(reReadCount).toBeGreaterThanOrEqual(2)
  })

  it('gone, and the re-read also answers 404: shows SCR-08 instead', async () => {
    changeHandler = (url, init) => {
      if (url.includes(`/api/v1/boards/${boardId}/cards`) && init?.method === 'POST') {
        return Promise.resolve(
          problem(404, { code: 'boards.not_available', title: 'That column no longer exists.' }),
        )
      }
      return undefined
    }
    let reads = 0
    openHandler = () => {
      reads += 1
      return reads === 1
        ? Promise.resolve(jsonResponse(200, aBoard()))
        : Promise.resolve(notAvailable())
    }

    const { title } = await openForm()
    await userEvent.type(title, 'Doomed title')
    await userEvent.click(screen.getByRole('button', { name: /^add$/i }))

    expect(await screen.findByRole('heading', { name: /board not available/i })).toBeInTheDocument()
  })

  it('success 201: the title lands last in its column, the form clears and stays open', async () => {
    withAddCardHandler(() =>
      jsonResponse(201, {
        id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7d09',
        column_id: columnId,
        position: 1,
        title: 'Brand new card',
        content_version: 1,
      }),
    )

    const { title } = await openForm()
    await userEvent.type(title, 'Brand new card')
    await userEvent.click(screen.getByRole('button', { name: /^add$/i }))

    expect(await screen.findByRole('button', { name: 'Brand new card' })).toBeInTheDocument()
    // The form stays open, cleared, ready for the next card.
    expect(screen.getByLabelText(/^title$/i)).toHaveValue('')
  })
})

// ---- kept text across a sign-in (AC-28) --------------------------------------------------------

describe('the kept-text offer (AC-28, back on this board after SCR-01)', () => {
  it('offers to apply the kept add-card text again, resubmitting it as an ordinary change', async () => {
    keep(anAccount.id, {
      boardId,
      item: 'add_card',
      fields: { column_id: columnId, title: 'Kept title', description: 'Kept description' },
    })
    openHandler = () =>
      Promise.resolve(jsonResponse(200, aBoard({ is_owner: false, columns: [aColumn()] })))

    let posted = false
    changeHandler = (url, init) => {
      if (url.includes(`/api/v1/boards/${boardId}/cards`) && init?.method === 'POST') {
        posted = true
        return Promise.resolve(
          jsonResponse(201, {
            id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7d10',
            column_id: columnId,
            position: 1,
            title: 'Kept title',
            content_version: 1,
          }),
        )
      }
      return undefined
    }

    renderScreen()
    await screen.findByRole('heading', { name: /launch plan/i })

    expect(screen.getByText(/kept title/i)).toBeInTheDocument()
    await userEvent.click(screen.getByRole('button', { name: /apply again/i }))

    await waitFor(() => expect(posted).toBe(true))
  })
})
