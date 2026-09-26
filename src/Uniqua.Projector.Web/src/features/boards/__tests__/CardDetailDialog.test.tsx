import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { boardQueryKey, cardQueryKey, type Board, type Card } from '@/api/boards'
import { CardDetailDialog } from '@/features/boards/CardDetailDialog'

/**
 * T22 / SCR-05 «Card detail» + SCR-06 «Confirm card deletion» (AC-13, AC-14, AC-16, AC-18, AC-23).
 * Driven the same way `ColumnManagement.test.tsx` drives `ColumnRow`: the real `openCard` /
 * `editCard` / `deleteCard` transports (`api/boards.ts`) through a stubbed `fetch`, never a mocked
 * hook, so a refusal is exercised exactly as the server answers it.
 */

const boardId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a01'
const columnId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7c01'
const cardId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7d01'

function aCard(overrides: Partial<Card> = {}): Card {
  return {
    id: cardId,
    column_id: columnId,
    position: 0,
    title: 'Ship the release',
    description: '',
    content_version: 1,
    ...overrides,
  }
}

function aBoard(overrides: Partial<Board> = {}): Board {
  return {
    id: boardId,
    name: 'Launch plan',
    is_owner: false,
    column_layout_version: 1,
    columns: [{ id: columnId, name: 'To do', position: 0, name_version: 1 }],
    cards: [{ id: cardId, column_id: columnId, position: 0, title: 'Ship the release', content_version: 1 }],
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

function problem(status: number, body: Record<string, unknown>) {
  return {
    ok: false,
    status,
    json: async () => body,
    text: async () => JSON.stringify(body),
    headers: new Headers({ 'content-type': 'application/problem+json' }),
  } as unknown as Response
}

const cardPath = `/api/v1/boards/${boardId}/cards/${cardId}`
const boardPath = `/api/v1/boards/${boardId}`

let openCardHandler: () => Promise<Response> | undefined = () => undefined
let changeHandler: (url: string, init: RequestInit | undefined) => Promise<Response> | undefined =
  () => undefined
let boardReadHandler: () => Promise<Response> = () => Promise.resolve(jsonResponse(200, aBoard()))

const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  const method = init?.method ?? 'GET'

  if (url.endsWith(cardPath) && method === 'GET') {
    const answer = openCardHandler()
    if (answer !== undefined) {
      return answer
    }
  }

  if (url.endsWith(boardPath) && method === 'GET') {
    return boardReadHandler()
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
  openCardHandler = () => Promise.resolve(jsonResponse(200, aCard()))
  changeHandler = () => undefined
  boardReadHandler = () => Promise.resolve(jsonResponse(200, aBoard()))
  document.cookie = 'XSRF-TOKEN=a-token'
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function renderDialog(onCardGone = vi.fn(), onOpenChange = vi.fn(), board = aBoard(), onRefused = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  client.setQueryData(boardQueryKey(boardId), board)
  render(
    <QueryClientProvider client={client}>
      <CardDetailDialog
        boardId={boardId}
        cardId={cardId}
        summaryTitle="Ship the release"
        open
        onOpenChange={onOpenChange}
        onCardGone={onCardGone}
        onRefused={onRefused}
      />
    </QueryClientProvider>,
  )
  return { client, onCardGone, onOpenChange, onRefused }
}

// ---- loading ----------------------------------------------------------------------------------

describe('loading', () => {
  it('shows the summary title and a loading status line', () => {
    openCardHandler = () => new Promise(() => {})
    renderDialog()

    expect(screen.getByText('Ship the release')).toBeInTheDocument()
    expect(screen.getByText(/loading the card/i)).toBeInTheDocument()
  })
})

// ---- default reading (AC-16) -------------------------------------------------------------------

describe('reading', () => {
  it('shows the title and description as plain text, line breaks and spaces kept, no link', async () => {
    openCardHandler = () =>
      Promise.resolve(
        jsonResponse(
          200,
          aCard({ description: '<b>not bold</b>\nTwo   spaces https://example.com' }),
        ),
      )
    renderDialog()

    await screen.findByText('Ship the release')
    const description = await screen.findByText(/not bold/)
    expect(description.innerHTML).not.toContain('<b>')
    expect(description).toHaveClass('whitespace-pre-wrap')
    expect(screen.queryByRole('link')).not.toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^edit$/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /delete card/i })).toBeInTheDocument()
  })

  it('shows muted «No description» when the description is empty', async () => {
    openCardHandler = () => Promise.resolve(jsonResponse(200, aCard({ description: '' })))
    renderDialog()

    expect(await screen.findByText(/no description/i)).toBeInTheDocument()
  })
})

// ---- error --------------------------------------------------------------------------------------

describe('error', () => {
  it('shows the failed block', async () => {
    openCardHandler = () => Promise.resolve(problem(500, {}))
    renderDialog()

    expect(await screen.findByText(/we could not load this card/i)).toBeInTheDocument()
  })
})

// ---- gone (read) — AC-18b analogue for a card read -----------------------------------------------

describe('gone (read)', () => {
  it('closes and reports «That card no longer exists.» once the board still answers', async () => {
    openCardHandler = () => Promise.resolve(problem(404, { code: 'boards.not_available' }))
    boardReadHandler = () => Promise.resolve(jsonResponse(200, aBoard()))
    const { onCardGone, onOpenChange } = renderDialog()

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false))
    expect(onCardGone).toHaveBeenCalledWith(expect.stringMatching(/that card no longer exists/i))
  })
})

// ---- editing --------------------------------------------------------------------------------------

describe('editing', () => {
  async function openEditor() {
    const result = renderDialog()
    await screen.findByRole('button', { name: /^edit$/i })
    await userEvent.click(screen.getByRole('button', { name: /^edit$/i }))
    return result
  }

  it('shows Title input and Description textarea, prefilled, with hints and Save/Cancel', async () => {
    await openEditor()

    expect(screen.getByLabelText(/^title$/i)).toHaveValue('Ship the release')
    expect(screen.getByLabelText(/description/i)).toHaveValue('')
    expect(screen.getByText(/between 1 and 150 characters/i)).toBeInTheDocument()
    expect(screen.getByText(/at most 10,000 characters/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^save$/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^cancel$/i })).toBeInTheDocument()
  })
})

// ---- validation (AC-14) ---------------------------------------------------------------------------

describe('validation', () => {
  it('400 card_title_invalid: shows the refusal under the title field and keeps both fields', async () => {
    changeHandler = (url, init) => {
      if (url.endsWith(cardPath) && init?.method === 'PATCH') {
        return Promise.resolve(
          problem(400, {
            code: 'boards.card_title_invalid',
            title: 'The card title is not usable.',
            detail: 'A card title must be between 1 and 150 characters.',
          }),
        )
      }
      return undefined
    }

    renderDialog()
    await screen.findByRole('button', { name: /^edit$/i })
    await userEvent.click(screen.getByRole('button', { name: /^edit$/i }))

    const titleInput = screen.getByLabelText(/^title$/i)
    const descriptionInput = screen.getByLabelText(/description/i)
    await userEvent.clear(titleInput)
    await userEvent.type(descriptionInput, 'Kept text')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    expect(
      await screen.findByText(/card title must be between 1 and 150 characters/i),
    ).toBeInTheDocument()
    expect(screen.getByLabelText(/^title$/i)).toHaveValue('')
    expect(screen.getByLabelText(/description/i)).toHaveValue('Kept text')
  })
})

// ---- session-ended (AC-28) ------------------------------------------------------------------------

describe('session-ended (save)', () => {
  it('401: hands the typed title and description to the screen to keep, filed under edit_card', async () => {
    changeHandler = (url, init) =>
      url.endsWith(cardPath) && init?.method === 'PATCH'
        ? Promise.resolve(
            problem(401, { code: 'accounts.session_not_recognised', detail: 'Sign in to continue.' }),
          )
        : undefined

    const { onRefused } = renderDialog()
    await userEvent.click(await screen.findByRole('button', { name: /^edit$/i }))
    const titleInput = screen.getByLabelText(/^title$/i)
    await userEvent.clear(titleInput)
    await userEvent.type(titleInput, 'Typed title')
    await userEvent.type(screen.getByLabelText(/description/i), 'Typed description')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    await waitFor(() => expect(onRefused).toHaveBeenCalledTimes(1))
    expect(onRefused.mock.calls[0][1]).toEqual({
      boardId,
      item: 'edit_card',
      fields: {
        card_id: cardId,
        title: 'Typed title',
        description: 'Typed description',
        content_version: '1',
      },
    })
  })
})

// ---- stale save (AC-23) ---------------------------------------------------------------------------

describe('stale (save)', () => {
  it('409 card_changed: shows the current version, keeps typed text, and Save reapplies against the new version', async () => {
    let attempt = 0
    changeHandler = (url, init) => {
      if (url.endsWith(cardPath) && init?.method === 'PATCH') {
        attempt += 1
        if (attempt === 1) {
          return Promise.resolve(
            problem(409, {
              code: 'boards.card_changed',
              title: 'The card was changed.',
              detail: 'This card was changed since you opened it.',
              current_card: aCard({ title: 'Changed elsewhere', content_version: 2 }),
            }),
          )
        }
        const body = JSON.parse(String(init?.body)) as { content_version: number }
        expect(body.content_version).toBe(2)
        return Promise.resolve(jsonResponse(200, aCard({ title: 'My new title', content_version: 3 })))
      }
      return undefined
    }

    const { client } = renderDialog()
    await screen.findByRole('button', { name: /^edit$/i })
    await userEvent.click(screen.getByRole('button', { name: /^edit$/i }))

    const titleInput = screen.getByLabelText(/^title$/i)
    await userEvent.clear(titleInput)
    await userEvent.type(titleInput, 'My new title')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    expect(await screen.findByText(/changed since you opened it/i)).toBeInTheDocument()
    expect(screen.getByText('Changed elsewhere')).toBeInTheDocument()
    expect(screen.getByLabelText(/^title$/i)).toHaveValue('My new title')

    // the board cache was patched with the current card's summary (ADR 0018)
    const board = client.getQueryData<Board>(boardQueryKey(boardId))
    expect(board?.cards.find((card) => card.id === cardId)?.title).toBe('Changed elsewhere')

    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    await waitFor(() => expect(attempt).toBe(2))
    await screen.findByText('My new title')
  })
})

// ---- confirm delete step (SCR-06) ------------------------------------------------------------------

describe('confirm delete step', () => {
  it('shows «Delete this card?», the title, the warning, and destructive Delete card / Cancel', async () => {
    renderDialog()
    await screen.findByRole('button', { name: /^edit$/i })
    await userEvent.click(screen.getByRole('button', { name: /delete card/i }))

    expect(await screen.findByText(/delete this card\?/i)).toBeInTheDocument()
    expect(screen.getAllByText('Ship the release').length).toBeGreaterThan(0)
    expect(screen.getByText(/this cannot be undone/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^cancel$/i })).toBeInTheDocument()

    const confirmButton = screen.getByRole('button', { name: /^delete card$/i })
    expect(confirmButton).toHaveClass('bg-destructive')
  })
})

// ---- stale delete (AC-23) --------------------------------------------------------------------------

describe('stale (delete)', () => {
  it('409 card_changed: goes back to reading, patched, with the refusal line', async () => {
    changeHandler = (url, init) => {
      if (url.endsWith(cardPath) && init?.method === 'DELETE') {
        return Promise.resolve(
          problem(409, {
            code: 'boards.card_changed',
            title: 'The card was changed.',
            detail: 'This card was changed since you opened it.',
            current_card: aCard({ title: 'Changed elsewhere', content_version: 2 }),
          }),
        )
      }
      return undefined
    }

    renderDialog()
    await screen.findByRole('button', { name: /^edit$/i })
    await userEvent.click(screen.getByRole('button', { name: /delete card/i }))
    await screen.findByText(/delete this card\?/i)
    await userEvent.click(screen.getByRole('button', { name: /^delete card$/i }))

    expect(await screen.findByText(/changed since you opened it/i)).toBeInTheDocument()
    expect(await screen.findByText('Changed elsewhere')).toBeInTheDocument()
    // back to reading: the edit/delete controls of the reading step are shown again
    expect(screen.getByRole('button', { name: /^edit$/i })).toBeInTheDocument()
  })
})

// ---- gone delete (AC-18) --------------------------------------------------------------------------

describe('gone (delete)', () => {
  it('404: the dialog closes and the board is refreshed', async () => {
    changeHandler = (url, init) => {
      if (url.endsWith(cardPath) && init?.method === 'DELETE') {
        return Promise.resolve(problem(404, { code: 'boards.not_available' }))
      }
      return undefined
    }
    boardReadHandler = () => Promise.resolve(jsonResponse(200, aBoard({ cards: [] })))

    const { onOpenChange } = renderDialog()
    await screen.findByRole('button', { name: /^edit$/i })
    await userEvent.click(screen.getByRole('button', { name: /delete card/i }))
    await screen.findByText(/delete this card\?/i)
    await userEvent.click(screen.getByRole('button', { name: /^delete card$/i }))

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false))
  })
})

// ---- success delete (AC-18) ------------------------------------------------------------------------

describe('success (delete)', () => {
  it('204: the tile leaves its column, others keep their order', async () => {
    changeHandler = (url, init) => {
      if (url.endsWith(cardPath) && init?.method === 'DELETE') {
        return Promise.resolve({
          ok: true,
          status: 204,
          json: async () => undefined,
          text: async () => '',
          headers: new Headers(),
        } as unknown as Response)
      }
      return undefined
    }

    const otherCardId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7d02'
    const board = aBoard({
      cards: [
        { id: cardId, column_id: columnId, position: 0, title: 'Ship the release', content_version: 1 },
        { id: otherCardId, column_id: columnId, position: 1, title: 'Other card', content_version: 1 },
      ],
    })

    const { client, onOpenChange } = renderDialog(vi.fn(), vi.fn(), board)
    await screen.findByRole('button', { name: /^edit$/i })
    await userEvent.click(screen.getByRole('button', { name: /delete card/i }))
    await screen.findByText(/delete this card\?/i)
    await userEvent.click(screen.getByRole('button', { name: /^delete card$/i }))

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false))
    const patched = client.getQueryData<Board>(boardQueryKey(boardId))
    expect(patched?.cards.map((card) => card.id)).toEqual([otherCardId])
  })
})

// ---- success (edit / AC-13) ------------------------------------------------------------------------

describe('success (save)', () => {
  it('200: shows the new text, and every next open sees it (the card cache is patched)', async () => {
    changeHandler = (url, init) => {
      if (url.endsWith(cardPath) && init?.method === 'PATCH') {
        return Promise.resolve(
          jsonResponse(200, aCard({ title: 'New title', description: 'New description', content_version: 2 })),
        )
      }
      return undefined
    }

    const { client } = renderDialog()
    await screen.findByRole('button', { name: /^edit$/i })
    await userEvent.click(screen.getByRole('button', { name: /^edit$/i }))

    const titleInput = screen.getByLabelText(/^title$/i)
    await userEvent.clear(titleInput)
    await userEvent.type(titleInput, 'New title')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    await screen.findByText('New title')
    expect(screen.getByText('New description')).toBeInTheDocument()

    const cached = client.getQueryData<Card>(cardQueryKey(boardId, cardId))
    expect(cached?.title).toBe('New title')
  })
})
