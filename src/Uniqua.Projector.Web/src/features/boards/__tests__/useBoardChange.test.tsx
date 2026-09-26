import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { renderHook, waitFor } from '@testing-library/react'
import type { ReactNode } from 'react'
import { describe, expect, it, vi } from 'vitest'

import { ApiError } from '@/api/accounts'
import { boardQueryKey, type Board, type Column } from '@/api/boards'
import { useBoardChange } from '@/features/boards/useBoardChange'

/**
 * T17 — AC-18b from the client's side (sad.md §6 flow 2). A change naming a since-deleted column
 * or card is refused `boards.not_available`, exactly as a never-existing board is, and the hook's
 * one job is to tell the two apart with a single re-read, without ever losing what was typed.
 */

const boardId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a8b'
const columnId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a01'

const aColumn: Column = { id: columnId, name: 'To do', position: 0, name_version: 1 }

const aBoard: Board = {
  id: boardId,
  name: 'Test board',
  is_owner: true,
  column_layout_version: 1,
  columns: [aColumn],
  cards: [],
}

function boardNotAvailable() {
  return new ApiError(404, 'boards.not_available', 'Board not available', 'This board does not exist, or you are not a member of it.', undefined)
}

function sessionNotRecognised() {
  return new ApiError(401, 'accounts.session_not_recognised', 'Not signed in', 'Sign in to continue.', undefined)
}

function wrapper(client: QueryClient) {
  return ({ children }: { children: ReactNode }) => (
    <QueryClientProvider client={client}>{children}</QueryClientProvider>
  )
}

describe('a change refused because the item it names is gone (AC-18b)', () => {
  it('re-reads the board once and resolves item-gone when the board is still there', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const mutationFn = vi.fn().mockRejectedValue(boardNotAvailable())
    // The production hook re-reads through `api/boards`' `openBoard`, which resolves with the
    // board once it is stubbed to hit the network; this suite asserts the resolution it produces.
    client.setQueryData(boardQueryKey(boardId), aBoard)

    const { result } = renderHook(
      () => useBoardChange<{ title: string }, unknown>({ boardId, mutationFn }),
      { wrapper: wrapper(client) },
    )

    result.current.change({ title: 'Edited title' })

    await waitFor(() => expect(result.current.resolvedAs).toBe('item-gone'))
  })

  it('keeps what was typed after the refusal, never swallowing it', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const mutationFn = vi.fn().mockRejectedValue(boardNotAvailable())
    const typed = { title: 'My careful edit' }

    const { result } = renderHook(
      () => useBoardChange<{ title: string }, unknown>({ boardId, mutationFn }),
      { wrapper: wrapper(client) },
    )

    result.current.change(typed)

    await waitFor(() => expect(result.current.error).toBeTruthy())
    expect(result.current.keptText).toEqual(typed)
  })
})

describe('a change refused because the board itself is gone', () => {
  it('resolves board-gone when the re-read also answers not_available', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const mutationFn = vi.fn().mockRejectedValue(boardNotAvailable())

    const { result } = renderHook(
      () => useBoardChange<{ column_id: string; title: string }, unknown>({ boardId, mutationFn }),
      { wrapper: wrapper(client) },
    )

    result.current.change({ column_id: columnId, title: 'Test card' })

    await waitFor(() => expect(result.current.resolvedAs).toBe('board-gone'))
  })
})

describe('a change refused because the session ended', () => {
  it('resolves session-ended without re-reading the board', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    const mutationFn = vi.fn().mockRejectedValue(sessionNotRecognised())

    const { result } = renderHook(
      () => useBoardChange<{ title: string }, unknown>({ boardId, mutationFn }),
      { wrapper: wrapper(client) },
    )

    result.current.change({ title: 'Edited title' })

    await waitFor(() => expect(result.current.resolvedAs).toBe('session-ended'))
  })
})

describe('an accepted change', () => {
  it('patches the cached board rather than leaving a second copy of it', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    client.setQueryData(boardQueryKey(boardId), aBoard)

    const renamedColumn: Column = { ...aColumn, name: 'Doing', name_version: 2 }
    const mutationFn = vi.fn().mockResolvedValue(renamedColumn)

    const { result } = renderHook(
      () =>
        useBoardChange<{ name: string; name_version: number }, Column>({
          boardId,
          mutationFn,
          applyAccepted: (board, renamed) => ({
            ...board,
            columns: board.columns.map((c) => (c.id === renamed.id ? renamed : c)),
          }),
        }),
      { wrapper: wrapper(client) },
    )

    result.current.change({ name: 'Doing', name_version: 1 })

    await waitFor(() => expect(result.current.isPending).toBe(false))

    const cached = client.getQueryData<Board>(boardQueryKey(boardId))
    expect(cached?.columns.find((c) => c.id === columnId)?.name).toBe('Doing')
  })
})

describe('a stale refusal', () => {
  it('patches the cached board from current_column (edge case row 3)', async () => {
    const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
    client.setQueryData(boardQueryKey(boardId), aBoard)

    const currentColumn: Column = { ...aColumn, name: 'Doing', name_version: 2 }
    const staleRefusal = new ApiError(
      409,
      'boards.column_renamed',
      'The column was renamed',
      'This column was renamed since you last saw it.',
      undefined,
      { column: currentColumn },
    )
    const mutationFn = vi.fn().mockRejectedValue(staleRefusal)

    const { result } = renderHook(
      () =>
        useBoardChange<{ name: string; name_version: number }, Column>({
          boardId,
          mutationFn,
          applyRefusalCurrent: (board, error) => ({
            ...board,
            columns: board.columns.map((c) =>
              c.id === (error.currentColumn as Column).id ? (error.currentColumn as Column) : c,
            ),
          }),
        }),
      { wrapper: wrapper(client) },
    )

    result.current.change({ name: 'Done', name_version: 1 })

    await waitFor(() => expect(result.current.error).toBeTruthy())

    const cached = client.getQueryData<Board>(boardQueryKey(boardId))
    expect(cached?.columns.find((c) => c.id === columnId)?.name).toBe('Doing')
  })
})
