import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import {
  ApiError,
  addCard,
  addColumn,
  createBoard,
  deleteBoard,
  deleteCard,
  deleteColumn,
  editCard,
  listMyBoards,
  moveColumn,
  openBoard,
  openCard,
  patchBoard,
  patchBoardFromCurrent,
  patchCard,
  renameBoard,
  renameColumn,
  type Board,
  type Card,
  type Column,
} from '@/api/boards'

/**
 * T17 — the boards transport, its query keys and its cache patches, written against
 * `docs/features/boards-columns-cards/contracts/openapi.yaml`. Each of the 13 operations is
 * checked for the method, path and body the contract names, and every state-changing one for the
 * antiforgery header every change requires (sad.md §8).
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

function noContent() {
  return {
    ok: true,
    status: 204,
    json: async () => {
      throw new Error('a 204 has no body to read')
    },
    text: async () => '',
    headers: new Headers(),
  } as unknown as Response
}

function initOf(call: number): RequestInit {
  return (fetchMock.mock.calls[call][1] ?? {}) as RequestInit
}

function pathOf(call: number): unknown {
  return fetchMock.mock.calls[call][0]
}

function bodyOf(call: number): unknown {
  return JSON.parse(initOf(call).body as string)
}

function headerOf(call: number, name: string): string | null {
  return new Headers(initOf(call).headers).get(name)
}

const boardId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a8b'
const columnId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a01'
const cardId = '0192f3a1-0000-7e91-a0b2-3c4d5e6f7c01'

const aColumn: Column = { id: columnId, name: 'To do', position: 0, name_version: 1 }

const aBoard: Board = {
  id: boardId,
  name: 'Test board',
  is_owner: true,
  column_layout_version: 1,
  columns: [aColumn],
  cards: [],
}

const aCard: Card = {
  id: cardId,
  column_id: columnId,
  position: 0,
  title: 'Test card',
  description: 'First line',
  content_version: 1,
}

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
  document.cookie = 'XSRF-TOKEN=the-issued-token'
})

afterEach(() => {
  vi.unstubAllGlobals()
})

describe('listMyBoards', () => {
  it('reads the caller\'s boards from the contract path and shape', async () => {
    const page = { items: [], has_next: false, has_prev: false, next_cursor: null, prev_cursor: null }
    fetchMock.mockResolvedValue(jsonResponse(200, page))

    const result = await listMyBoards()

    expect(pathOf(0)).toBe('/api/v1/boards')
    expect(initOf(0).method ?? 'GET').toBe('GET')
    expect(result).toEqual(page)
  })
})

describe('createBoard', () => {
  it('sends the name and the antiforgery token, and parses the created board', async () => {
    fetchMock.mockResolvedValue(jsonResponse(201, aBoard))

    const result = await createBoard({ name: 'Test board' })

    expect(pathOf(0)).toBe('/api/v1/boards')
    expect(initOf(0).method).toBe('POST')
    expect(bodyOf(0)).toEqual({ name: 'Test board' })
    expect(headerOf(0, 'X-XSRF-TOKEN')).toBe('the-issued-token')
    expect(result).toEqual(aBoard)
  })
})

describe('openBoard', () => {
  it('reads the board by id and parses it', async () => {
    fetchMock.mockResolvedValue(jsonResponse(200, aBoard))

    const result = await openBoard(boardId)

    expect(pathOf(0)).toBe(`/api/v1/boards/${boardId}`)
    expect(initOf(0).method ?? 'GET').toBe('GET')
    expect(result).toEqual(aBoard)
  })
})

describe('renameBoard', () => {
  it('sends the new name and parses the renamed board name', async () => {
    const renamed = { id: boardId, name: 'Test board, renamed' }
    fetchMock.mockResolvedValue(jsonResponse(200, renamed))

    const result = await renameBoard(boardId, { name: 'Test board, renamed' })

    expect(pathOf(0)).toBe(`/api/v1/boards/${boardId}`)
    expect(initOf(0).method).toBe('PATCH')
    expect(bodyOf(0)).toEqual({ name: 'Test board, renamed' })
    expect(headerOf(0, 'X-XSRF-TOKEN')).toBe('the-issued-token')
    expect(result).toEqual(renamed)
  })
})

describe('deleteBoard', () => {
  it('sends the typed confirmation name and reads no body on success', async () => {
    fetchMock.mockResolvedValue(noContent())

    await deleteBoard(boardId, { confirm_name: 'Test board' })

    expect(pathOf(0)).toBe(`/api/v1/boards/${boardId}`)
    expect(initOf(0).method).toBe('DELETE')
    expect(bodyOf(0)).toEqual({ confirm_name: 'Test board' })
    expect(headerOf(0, 'X-XSRF-TOKEN')).toBe('the-issued-token')
  })
})

describe('addColumn', () => {
  it('sends the name and parses the added column', async () => {
    const added = { column: aColumn, column_layout_version: 2 }
    fetchMock.mockResolvedValue(jsonResponse(201, added))

    const result = await addColumn(boardId, { name: 'Review' })

    expect(pathOf(0)).toBe(`/api/v1/boards/${boardId}/columns`)
    expect(initOf(0).method).toBe('POST')
    expect(bodyOf(0)).toEqual({ name: 'Review' })
    expect(headerOf(0, 'X-XSRF-TOKEN')).toBe('the-issued-token')
    expect(result).toEqual(added)
  })
})

describe('renameColumn', () => {
  it('sends the name and the name_version it saw, and parses the renamed column', async () => {
    const renamed = { id: columnId, name: 'Doing', position: 0, name_version: 2 }
    fetchMock.mockResolvedValue(jsonResponse(200, renamed))

    const result = await renameColumn(boardId, columnId, { name: 'Doing', name_version: 1 })

    expect(pathOf(0)).toBe(`/api/v1/boards/${boardId}/columns/${columnId}`)
    expect(initOf(0).method).toBe('PATCH')
    expect(bodyOf(0)).toEqual({ name: 'Doing', name_version: 1 })
    expect(headerOf(0, 'X-XSRF-TOKEN')).toBe('the-issued-token')
    expect(result).toEqual(renamed)
  })
})

describe('moveColumn', () => {
  it('sends the new position and the layout version it saw, and parses the new layout', async () => {
    const layout = { column_layout_version: 2, columns: [aColumn] }
    fetchMock.mockResolvedValue(jsonResponse(200, layout))

    const result = await moveColumn(boardId, columnId, { position: 0, column_layout_version: 1 })

    expect(pathOf(0)).toBe(`/api/v1/boards/${boardId}/columns/${columnId}/position`)
    expect(initOf(0).method).toBe('PUT')
    expect(bodyOf(0)).toEqual({ position: 0, column_layout_version: 1 })
    expect(headerOf(0, 'X-XSRF-TOKEN')).toBe('the-issued-token')
    expect(result).toEqual(layout)
  })
})

describe('deleteColumn', () => {
  it('sends the name_version it saw, and parses the board\'s new column set', async () => {
    const layout = { column_layout_version: 3, columns: [] }
    fetchMock.mockResolvedValue(jsonResponse(200, layout))

    const result = await deleteColumn(boardId, columnId, { name_version: 1 })

    expect(pathOf(0)).toBe(`/api/v1/boards/${boardId}/columns/${columnId}`)
    expect(initOf(0).method).toBe('DELETE')
    expect(bodyOf(0)).toEqual({ name_version: 1 })
    expect(headerOf(0, 'X-XSRF-TOKEN')).toBe('the-issued-token')
    expect(result).toEqual(layout)
  })
})

describe('addCard', () => {
  it('sends the column, title and description, and parses the added card summary', async () => {
    const added = { id: cardId, column_id: columnId, position: 0, title: 'Test card', content_version: 1 }
    fetchMock.mockResolvedValue(jsonResponse(201, added))

    const result = await addCard(boardId, {
      column_id: columnId,
      title: 'Test card',
      description: 'First line',
    })

    expect(pathOf(0)).toBe(`/api/v1/boards/${boardId}/cards`)
    expect(initOf(0).method).toBe('POST')
    expect(bodyOf(0)).toEqual({ column_id: columnId, title: 'Test card', description: 'First line' })
    expect(headerOf(0, 'X-XSRF-TOKEN')).toBe('the-issued-token')
    expect(result).toEqual(added)
  })
})

describe('openCard', () => {
  it('reads the card by id and parses its full text', async () => {
    fetchMock.mockResolvedValue(jsonResponse(200, aCard))

    const result = await openCard(boardId, cardId)

    expect(pathOf(0)).toBe(`/api/v1/boards/${boardId}/cards/${cardId}`)
    expect(initOf(0).method ?? 'GET').toBe('GET')
    expect(result).toEqual(aCard)
  })
})

describe('editCard', () => {
  it('sends the changed fields and the content_version it saw, and parses the saved card', async () => {
    const saved = { ...aCard, title: 'Test card, edited', content_version: 2 }
    fetchMock.mockResolvedValue(jsonResponse(200, saved))

    const result = await editCard(boardId, cardId, { title: 'Test card, edited', content_version: 1 })

    expect(pathOf(0)).toBe(`/api/v1/boards/${boardId}/cards/${cardId}`)
    expect(initOf(0).method).toBe('PATCH')
    expect(bodyOf(0)).toEqual({ title: 'Test card, edited', content_version: 1 })
    expect(headerOf(0, 'X-XSRF-TOKEN')).toBe('the-issued-token')
    expect(result).toEqual(saved)
  })
})

describe('deleteCard', () => {
  it('sends the content_version it saw, and reads no body on success', async () => {
    fetchMock.mockResolvedValue(noContent())

    await deleteCard(boardId, cardId, { content_version: 1 })

    expect(pathOf(0)).toBe(`/api/v1/boards/${boardId}/cards/${cardId}`)
    expect(initOf(0).method).toBe('DELETE')
    expect(bodyOf(0)).toEqual({ content_version: 1 })
    expect(headerOf(0, 'X-XSRF-TOKEN')).toBe('the-issued-token')
  })
})

describe('a refusal', () => {
  it('carries the code and the current_column when the column was renamed since (AC-06b)', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse(409, {
        code: 'boards.column_renamed',
        title: 'The column was renamed',
        detail: 'This column was renamed since you last saw it.',
        current_column: { id: columnId, name: 'Doing', position: 1, name_version: 2 },
      }),
    )

    const failure = await renameColumn(boardId, columnId, { name: 'Done', name_version: 1 }).catch(
      (error: unknown) => error,
    )

    expect(failure).toBeInstanceOf(ApiError)
    expect((failure as ApiError).code).toBe('boards.column_renamed')
    expect((failure as ApiError).currentColumn).toEqual({
      id: columnId,
      name: 'Doing',
      position: 1,
      name_version: 2,
    })
  })

  it('carries the code and the current_card when the card was changed since (AC-23)', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse(409, {
        code: 'boards.card_changed',
        title: 'The card was changed',
        detail: 'This card was changed since you opened it.',
        current_card: { ...aCard, title: 'Edited elsewhere', content_version: 3 },
      }),
    )

    const failure = await editCard(boardId, cardId, { title: 'Mine', content_version: 1 }).catch(
      (error: unknown) => error,
    )

    expect(failure).toBeInstanceOf(ApiError)
    expect((failure as ApiError).code).toBe('boards.card_changed')
    expect((failure as ApiError).currentCard).toEqual({ ...aCard, title: 'Edited elsewhere', content_version: 3 })
  })

  it('carries the one identical boards.not_available code for a since-deleted column or card (AC-18b)', async () => {
    fetchMock.mockResolvedValue(
      jsonResponse(404, {
        code: 'boards.not_available',
        title: 'Board not available',
        detail: 'This board does not exist, or you are not a member of it.',
      }),
    )

    const failure = await deleteCard(boardId, cardId, { content_version: 1 }).catch(
      (error: unknown) => error,
    )

    expect(failure).toBeInstanceOf(ApiError)
    expect((failure as ApiError).code).toBe('boards.not_available')
    expect((failure as ApiError).status).toBe(404)
  })
})

describe('cache patches', () => {
  it('patchBoard applies an accepted column rename onto the cached board', () => {
    const renamedColumn: Column = { ...aColumn, name: 'Doing', name_version: 2 }

    const patched = patchBoard(aBoard, { kind: 'column-renamed', column: renamedColumn })

    expect(patched.columns.find((c) => c.id === columnId)?.name).toBe('Doing')
    expect(patched.columns.find((c) => c.id === columnId)?.name_version).toBe(2)
  })

  it('patchBoardFromCurrent applies a current_column payload onto the cached board (edge case row 3)', () => {
    const currentColumn: Column = { ...aColumn, name: 'Doing', name_version: 2 }

    const patched = patchBoardFromCurrent(aBoard, { current_column: currentColumn })

    expect(patched.columns.find((c) => c.id === columnId)?.name).toBe('Doing')
    expect(patched.columns.find((c) => c.id === columnId)?.name_version).toBe(2)
  })

  it('patchCard applies an accepted edit onto the cached card', () => {
    const patched = patchCard(aCard, { title: 'Edited', content_version: 2 })

    expect(patched.title).toBe('Edited')
    expect(patched.content_version).toBe(2)
  })
})
