import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { MemoryRouter, Route, Routes } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { boardQueryKey, boardsQueryKey, type Board, type BoardListPage } from '@/api/boards'
import { DeleteBoardDialog } from '@/features/boards/DeleteBoardDialog'

/**
 * T22 / SCR-07 «Confirm board deletion» (AC-20, AC-20b). Driven the same way `ColumnManagement` and
 * `CardDetailDialog` are: the real `deleteBoard` / `openBoard` transports through a stubbed `fetch`.
 */

const boardId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a01'

function aBoard(overrides: Partial<Board> = {}): Board {
  return {
    id: boardId,
    name: 'Q4 launch',
    is_owner: true,
    column_layout_version: 1,
    columns: [],
    cards: [],
    ...overrides,
  }
}

function aListPage(overrides: Partial<BoardListPage> = {}): BoardListPage {
  return {
    items: [{ id: boardId, name: 'Q4 launch', created_at: '2026-01-01T00:00:00Z', is_owner: true }],
    has_next: false,
    has_prev: false,
    next_cursor: null,
    prev_cursor: null,
    ...overrides,
  }
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

const boardPath = `/api/v1/boards/${boardId}`

let deleteHandler: () => Promise<Response> | undefined = () => undefined
let boardReadHandler: () => Promise<Response> = () => Promise.resolve(jsonResponse(200, aBoard()))

const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  const method = init?.method ?? 'GET'

  if (url.endsWith(boardPath) && method === 'DELETE') {
    const answer = deleteHandler()
    if (answer !== undefined) {
      return answer
    }
  }

  if (url.endsWith(boardPath) && method === 'GET') {
    return boardReadHandler()
  }

  return Promise.resolve(problem(404, {}))
})

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockClear()
  deleteHandler = () => undefined
  boardReadHandler = () => Promise.resolve(jsonResponse(200, aBoard()))
  document.cookie = 'XSRF-TOKEN=a-token'
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function renderDialog(board = aBoard(), onOpenChange = vi.fn(), onRefused = vi.fn(), initialConfirmName?: string) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  client.setQueryData(boardQueryKey(boardId), board)
  client.setQueryData(boardsQueryKey, aListPage())

  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={[`/boards/${boardId}`]}>
        <Routes>
          <Route
            path="/boards/:boardId"
            element={
              <DeleteBoardDialog
                board={board}
                open
                onOpenChange={onOpenChange}
                onRefused={onRefused}
                initialConfirmName={initialConfirmName}
              />
            }
          />
          <Route path="/" element={<p>My boards</p>} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )
  return { client, onOpenChange, onRefused }
}

// ---- always-enabled confirm --------------------------------------------------------------------

describe('confirm control', () => {
  it('shows the warning, the name in bold, the label + input, and «Delete board» always enabled', () => {
    renderDialog()

    expect(
      screen.getByText(/deletes the board with all its columns and cards.*cannot be undone/i),
    ).toBeInTheDocument()
    expect(screen.getByText('Q4 launch')).toBeInTheDocument()
    const input = screen.getByLabelText(/type the board's name to confirm/i)
    expect(input).toHaveValue('')

    const deleteButton = screen.getByRole('button', { name: /^delete board$/i })
    expect(deleteButton).toBeEnabled()
    expect(deleteButton).toHaveClass('bg-destructive')
  })
})

// ---- mismatch (AC-20b) --------------------------------------------------------------------------

describe('mismatch', () => {
  it('409 confirmation_mismatch: shows the refusal, replaces the shown name with current_name, patches the board cache, keeps the typed text', async () => {
    deleteHandler = () =>
      Promise.resolve(
        problem(409, {
          code: 'boards.confirmation_mismatch',
          title: 'The name does not match.',
          detail: 'The name typed must match the board’s current name exactly.',
          current_name: 'Renamed elsewhere',
        }),
      )

    const { client } = renderDialog()
    const input = screen.getByLabelText(/type the board's name to confirm/i)
    await userEvent.type(input, 'q4 launch')
    await userEvent.click(screen.getByRole('button', { name: /^delete board$/i }))

    expect(await screen.findByText(/does not match/i)).toBeInTheDocument()
    expect(screen.getByText('Renamed elsewhere')).toBeInTheDocument()
    expect(screen.queryByText('Q4 launch')).not.toBeInTheDocument()
    expect(screen.getByLabelText(/type the board's name to confirm/i)).toHaveValue('q4 launch')

    const board = client.getQueryData<Board>(boardQueryKey(boardId))
    expect(board?.name).toBe('Renamed elsewhere')
  })
})

// ---- owner-only ------------------------------------------------------------------------------

describe('owner-only', () => {
  it('403 owner_only: shows the refusal, then closes once the re-read says is_owner: false', async () => {
    deleteHandler = () =>
      Promise.resolve(
        problem(403, {
          code: 'boards.owner_only',
          title: 'Only the owner can do that.',
          detail: 'Only the board’s owner can delete it.',
        }),
      )
    boardReadHandler = () => Promise.resolve(jsonResponse(200, aBoard({ is_owner: false })))

    const onOpenChange = vi.fn()
    renderDialog(aBoard(), onOpenChange)
    const input = screen.getByLabelText(/type the board's name to confirm/i)
    await userEvent.type(input, 'Q4 launch')
    await userEvent.click(screen.getByRole('button', { name: /^delete board$/i }))

    expect(await screen.findByText(/only the board.s owner can delete it/i)).toBeInTheDocument()
    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false))
  })
})

// ---- not-available ---------------------------------------------------------------------------

describe('not-available', () => {
  it('404: the dialog closes (SCR-08 takes over from the board query)', async () => {
    deleteHandler = () => Promise.resolve(problem(404, { code: 'boards.not_available' }))
    boardReadHandler = () => Promise.resolve(problem(404, { code: 'boards.not_available' }))

    const onOpenChange = vi.fn()
    renderDialog(aBoard(), onOpenChange)
    const input = screen.getByLabelText(/type the board's name to confirm/i)
    await userEvent.type(input, 'Q4 launch')
    await userEvent.click(screen.getByRole('button', { name: /^delete board$/i }))

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false))
  })
})

// ---- session-ended (AC-28) --------------------------------------------------------------------

describe('session-ended', () => {
  it('401: hands the typed name to the screen to keep, filed under delete_board', async () => {
    deleteHandler = () =>
      Promise.resolve(problem(401, { code: 'accounts.session_not_recognised', detail: 'Sign in to continue.' }))

    const { onRefused } = renderDialog()
    await userEvent.type(screen.getByLabelText(/type the board's name to confirm/i), 'Q4 laun')
    await userEvent.click(screen.getByRole('button', { name: /^delete board$/i }))

    await waitFor(() => expect(onRefused).toHaveBeenCalledTimes(1))
    expect(onRefused.mock.calls[0][1]).toEqual({
      boardId,
      item: 'delete_board',
      fields: { confirm_name: 'Q4 laun' },
    })
  })

  it('reopened by «Apply again»: starts with the kept name filled in, and deletes nothing by itself', () => {
    renderDialog(aBoard(), vi.fn(), vi.fn(), 'Q4 laun')

    expect(screen.getByLabelText(/type the board's name to confirm/i)).toHaveValue('Q4 laun')
    expect(fetchMock).not.toHaveBeenCalled()
  })
})

// ---- success (AC-20) --------------------------------------------------------------------------

describe('success', () => {
  it('204: navigates to the board list, the list cache drops the board, and its query is removed', async () => {
    deleteHandler = () => Promise.resolve(noContent())

    const { client } = renderDialog()
    const input = screen.getByLabelText(/type the board's name to confirm/i)
    await userEvent.type(input, 'Q4 launch')
    await userEvent.click(screen.getByRole('button', { name: /^delete board$/i }))

    await screen.findByText('My boards')

    const page = client.getQueryData<BoardListPage>(boardsQueryKey)
    expect(page?.items.some((entry) => entry.id === boardId)).toBe(false)
    expect(client.getQueryData(boardQueryKey(boardId))).toBeUndefined()
  })
})
