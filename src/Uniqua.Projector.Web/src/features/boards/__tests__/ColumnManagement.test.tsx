import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query'
import { fireEvent, render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { boardQueryKey, type Board, type Column } from '@/api/boards'
import { ColumnRow } from '@/features/boards/ColumnRow'
import type { KeptDraft } from '@/features/boards/draftStore'

/**
 * T21 / SCR-04 «Add column» + «Column header». Every test drives the real column transports
 * (`addColumn` / `renameColumn` / `moveColumn` / `deleteColumn`, from `api/boards.ts`) through a
 * stubbed `fetch`, the same way `BoardScreen.test.tsx` drives `renameBoard` / `addCard` — a refusal
 * is exercised as the server actually answers it, never as a mocked hook.
 *
 * `ColumnRow`'s `board` prop always comes from `BoardScreen`'s query cache (T20/T22), so `Harness`
 * reads it the same way, through `useQuery` keyed by `boardQueryKey`, so a success or a stale
 * refusal that patches the cache is seen exactly as it would be on the real screen.
 */

const boardId = '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a01'

const columnA: Column = { id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7c01', name: 'To do', position: 0, name_version: 1 }
const columnB: Column = { id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7c02', name: 'Doing', position: 1, name_version: 1 }
const columnC: Column = { id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7c03', name: 'Done', position: 2, name_version: 1 }

const cardOnB = {
  id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7d01',
  column_id: columnB.id,
  position: 0,
  title: 'A card',
  content_version: 1,
}

function aBoard(overrides: Partial<Board> = {}): Board {
  return {
    id: boardId,
    name: 'Launch plan',
    is_owner: false,
    column_layout_version: 1,
    columns: [columnA, columnB, columnC],
    cards: [cardOnB],
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

let changeHandler: (url: string, init: RequestInit | undefined) => Promise<Response> | undefined =
  () => undefined

const fetchMock = vi.fn((input: RequestInfo | URL, init?: RequestInit) => {
  const url = String(input)
  const changed = changeHandler(url, init)
  if (changed !== undefined) {
    return changed
  }
  return Promise.resolve(problem(404, {}))
})

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockClear()
  changeHandler = () => undefined
  document.cookie = 'XSRF-TOKEN=a-token'
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function Harness({
  board,
  onRefused,
}: {
  board: Board
  onRefused: (error: unknown, kept: KeptDraft) => void
}) {
  const query = useQuery({
    queryKey: boardQueryKey(board.id),
    queryFn: () => Promise.resolve(board),
    initialData: board,
  })
  return <ColumnRow board={query.data ?? board} onRefused={onRefused} />
}

function renderRow(board: Board, onRefused = vi.fn()) {
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  client.setQueryData(boardQueryKey(board.id), board)
  render(
    <QueryClientProvider client={client}>
      <Harness board={board} onRefused={onRefused} />
    </QueryClientProvider>,
  )
  return { client, onRefused }
}

const columnsPath = `/api/v1/boards/${boardId}/columns`
const columnPath = (columnId: string) => `${columnsPath}/${columnId}`

// ---- add column (AC-05, AC-08, AC-11) --------------------------------------------------------------

describe('Add column', () => {
  function withAddHandler(answer: () => Response) {
    changeHandler = (url, init) => {
      if (url.endsWith(columnsPath) && init?.method === 'POST') {
        return Promise.resolve(answer())
      }
      return undefined
    }
  }

  async function openForm() {
    renderRow(aBoard())
    await userEvent.click(screen.getByRole('button', { name: /\+\s*add column/i }))
    return screen.getByLabelText(/column name/i)
  }

  it('editing: shows the name field empty, a 1-50 hint, and Add / Cancel', async () => {
    const input = await openForm()

    expect(input).toHaveValue('')
    expect(screen.getByText(/between 1 and 50 characters/i)).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /^add$/i })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: /cancel/i })).toBeInTheDocument()
  })

  it('pending: «Add» reads «Adding…» and is disabled while the request is in flight', async () => {
    changeHandler = (url, init) => {
      if (url.endsWith(columnsPath) && init?.method === 'POST') {
        return new Promise(() => {})
      }
      return undefined
    }

    const input = await openForm()
    await userEvent.type(input, 'Review')
    await userEvent.click(screen.getByRole('button', { name: /^add$/i }))

    expect(await screen.findByRole('button', { name: /^adding…$/i })).toBeDisabled()
  })

  it('validation 400 column_name_invalid: shows the refusal line and keeps what was typed', async () => {
    withAddHandler(() =>
      problem(400, {
        code: 'boards.column_name_invalid',
        title: 'The column name is not usable.',
        detail: 'A column name must be between 1 and 50 characters.',
      }),
    )

    const input = await openForm()
    await userEvent.type(input, 'x'.repeat(51))
    await userEvent.click(screen.getByRole('button', { name: /^add$/i }))

    expect(await screen.findByText(/column name must be between 1 and 50 characters/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/column name/i)).toHaveValue('x'.repeat(51))
  })

  it('limit 409 column_limit_reached: shows the refusal with the ceiling, and the control stays', async () => {
    withAddHandler(() =>
      problem(409, {
        code: 'boards.column_limit_reached',
        title: 'This board is full.',
        detail: 'A board can hold at most 20 columns.',
      }),
    )

    const input = await openForm()
    await userEvent.type(input, 'One more')
    await userEvent.click(screen.getByRole('button', { name: /^add$/i }))

    expect(await screen.findByText(/a board can hold at most 20 columns/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/column name/i)).toHaveValue('One more')
  })

  it('success 201: the new column lands at the end of the row', async () => {
    withAddHandler(() =>
      jsonResponse(201, {
        column: { id: 'new-column-id', name: 'Review', position: 3, name_version: 1 },
        column_layout_version: 2,
      }),
    )

    const input = await openForm()
    await userEvent.type(input, 'Review')
    await userEvent.click(screen.getByRole('button', { name: /^add$/i }))

    await screen.findByText('Review')
    const handles = screen.getAllByRole('button', { name: /^move column /i })
    expect(handles.at(-1)).toHaveAccessibleName('Move column Review')
  })
})

// ---- column header: default (AC-06, AC-06b, AC-07 .. AC-11, AC-24) -------------------------------

describe('Column header', () => {
  it('shows the drag handle, the name, and Rename / Delete controls', () => {
    renderRow(aBoard({ columns: [columnA], cards: [] }))

    expect(screen.getByRole('button', { name: 'Move column To do' })).toBeInTheDocument()
    expect(screen.getByText('To do')).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Rename column' })).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Delete column' })).toBeInTheDocument()
  })
})

// ---- column rename (AC-06, AC-06b, AC-08) ---------------------------------------------------------

describe('Column rename', () => {
  function withRenameHandler(answer: () => Response) {
    changeHandler = (url, init) => {
      if (url.endsWith(columnPath(columnA.id)) && init?.method === 'PATCH') {
        return Promise.resolve(answer())
      }
      return undefined
    }
  }

  async function openEditor(onRefused = vi.fn()) {
    renderRow(aBoard({ columns: [columnA], cards: [] }), onRefused)
    await userEvent.click(screen.getByRole('button', { name: 'Rename column' }))
    return { input: screen.getByLabelText(/column name/i), onRefused }
  }

  it('editing: prefills the current name', async () => {
    const { input } = await openEditor()
    expect(input).toHaveValue('To do')
  })

  it('pending: shows "Saving…"', async () => {
    changeHandler = (url, init) => {
      if (url.endsWith(columnPath(columnA.id)) && init?.method === 'PATCH') {
        return new Promise(() => {})
      }
      return undefined
    }

    const { input } = await openEditor()
    await userEvent.clear(input)
    await userEvent.type(input, 'Backlog')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    expect(await screen.findByText(/saving/i)).toBeInTheDocument()
  })

  it('validation 400 column_name_invalid: shows the refusal and keeps what was typed', async () => {
    withRenameHandler(() =>
      problem(400, {
        code: 'boards.column_name_invalid',
        title: 'The column name is not usable.',
        detail: 'A column name must be between 1 and 50 characters.',
      }),
    )

    const { input } = await openEditor()
    await userEvent.clear(input)
    await userEvent.type(input, 'x'.repeat(51))
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    expect(await screen.findByText(/column name must be between 1 and 50 characters/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/column name/i)).toHaveValue('x'.repeat(51))
  })

  it('stale 409 column_renamed: patches the header, keeps the editor open with the typed name, and names the current name', async () => {
    withRenameHandler(() =>
      problem(409, {
        code: 'boards.column_renamed',
        title: 'The column was renamed.',
        detail: 'This column was renamed since you last saw it.',
        current_column: { id: columnA.id, name: 'Renamed elsewhere', position: 0, name_version: 2 },
      }),
    )

    const { input } = await openEditor()
    await userEvent.clear(input)
    await userEvent.type(input, 'My new name')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    expect(await screen.findByText(/renamed since you last saw it/i)).toBeInTheDocument()
    expect(screen.getByText(/renamed elsewhere/i)).toBeInTheDocument()
    expect(screen.getByLabelText(/column name/i)).toHaveValue('My new name')
  })

  it('stale, then Save again: sent with the patched name_version, and accepted', async () => {
    let attempt = 0
    changeHandler = (url, init) => {
      if (url.endsWith(columnPath(columnA.id)) && init?.method === 'PATCH') {
        attempt += 1
        if (attempt === 1) {
          return Promise.resolve(
            problem(409, {
              code: 'boards.column_renamed',
              title: 'The column was renamed.',
              detail: 'This column was renamed since you last saw it.',
              current_column: { id: columnA.id, name: 'Renamed elsewhere', position: 0, name_version: 2 },
            }),
          )
        }
        const body = JSON.parse(String(init?.body)) as { name: string; name_version: number }
        expect(body.name_version).toBe(2)
        return Promise.resolve(
          jsonResponse(200, { id: columnA.id, name: body.name, position: 0, name_version: 3 }),
        )
      }
      return undefined
    }

    const { input } = await openEditor()
    await userEvent.clear(input)
    await userEvent.type(input, 'My new name')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))
    await screen.findByText(/renamed since you last saw it/i)

    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    await waitFor(() => expect(attempt).toBe(2))
    expect(await screen.findByText('My new name')).toBeInTheDocument()
  })

  it('gone 404: reports the refusal upward with the typed name kept', async () => {
    withRenameHandler(() =>
      problem(404, { code: 'boards.not_available', title: 'That column no longer exists.' }),
    )

    const { input, onRefused } = await openEditor()
    await userEvent.clear(input)
    await userEvent.type(input, 'Orphaned name')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    await waitFor(() => expect(onRefused).toHaveBeenCalled())
    const [error, kept] = onRefused.mock.calls[0] as [unknown, KeptDraft]
    expect(error).toMatchObject({ status: 404, code: 'boards.not_available' })
    // Everything SCR-04 needs to tell the column is gone (AC-18b) or to apply the name again (AC-28).
    expect(kept).toEqual({
      boardId,
      item: 'rename_column',
      fields: { column_id: columnA.id, name: 'Orphaned name', name_version: String(columnA.name_version) },
    })
  })

  it('success: shows the new name and closes the editor', async () => {
    withRenameHandler(() =>
      jsonResponse(200, { id: columnA.id, name: 'Backlog', position: 0, name_version: 2 }),
    )

    const { input } = await openEditor()
    await userEvent.clear(input)
    await userEvent.type(input, 'Backlog')
    await userEvent.click(screen.getByRole('button', { name: /^save$/i }))

    expect(await screen.findByText('Backlog')).toBeInTheDocument()
    expect(screen.queryByLabelText(/column name/i)).not.toBeInTheDocument()
  })
})

// ---- column delete (AC-07, AC-09, AC-10, AC-06b) ---------------------------------------------------

describe('Column delete', () => {
  function withDeleteHandler(columnId: string, answer: () => Response) {
    changeHandler = (url, init) => {
      if (url.endsWith(columnPath(columnId)) && init?.method === 'DELETE') {
        return Promise.resolve(answer())
      }
      return undefined
    }
  }

  it('sends the request with no confirmation dialog, and the button is never pre-disabled', async () => {
    withDeleteHandler(columnA.id, () =>
      jsonResponse(200, { column_layout_version: 2, columns: [{ ...columnB, position: 0 }] }),
    )
    renderRow(aBoard({ columns: [columnA, columnB], cards: [] }))

    const deleteButtons = screen.getAllByRole('button', { name: 'Delete column' })
    expect(deleteButtons[0]).toBeEnabled()

    await userEvent.click(deleteButtons[0])

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(screen.queryByRole('dialog')).not.toBeInTheDocument()
    expect(screen.queryByRole('alertdialog')).not.toBeInTheDocument()
  })

  it('not-empty 409 column_not_empty: refuses, leaves the column and its card untouched (the trash was never disabled)', async () => {
    withDeleteHandler(columnB.id, () =>
      problem(409, {
        code: 'boards.column_not_empty',
        title: 'This column still holds cards.',
        detail: 'A column that still holds cards cannot be deleted.',
      }),
    )
    renderRow(aBoard({ columns: [columnA, columnB], cards: [cardOnB] }))

    const deleteButtons = screen.getAllByRole('button', { name: 'Delete column' })
    expect(deleteButtons[1]).toBeEnabled()
    await userEvent.click(deleteButtons[1])

    expect(
      await screen.findByText(/a column that still holds cards cannot be deleted/i),
    ).toBeInTheDocument()
    expect(screen.getByRole('button', { name: 'Move column Doing' })).toBeInTheDocument()
  })

  it('last-column 409 last_column: refuses to delete the only column', async () => {
    withDeleteHandler(columnA.id, () =>
      problem(409, {
        code: 'boards.last_column',
        title: 'A board must keep at least one column.',
        detail: 'A board must keep at least one column.',
      }),
    )
    renderRow(aBoard({ columns: [columnA], cards: [] }))

    await userEvent.click(screen.getByRole('button', { name: 'Delete column' }))

    expect(await screen.findByText(/a board must keep at least one column/i)).toBeInTheDocument()
  })

  it('stale 409 column_renamed: patches the header, shows the refusal, deletes nothing', async () => {
    withDeleteHandler(columnA.id, () =>
      problem(409, {
        code: 'boards.column_renamed',
        title: 'The column was renamed.',
        detail: 'This column was renamed since you last saw it.',
        current_column: { id: columnA.id, name: 'Renamed elsewhere', position: 0, name_version: 2 },
      }),
    )
    renderRow(aBoard({ columns: [columnA, columnB], cards: [] }))

    const deleteButtons = screen.getAllByRole('button', { name: 'Delete column' })
    await userEvent.click(deleteButtons[0])

    expect(await screen.findByText(/renamed since you last saw it/i)).toBeInTheDocument()
    expect(screen.getByText('Renamed elsewhere')).toBeInTheDocument()
    expect(screen.getAllByRole('button', { name: /^move column/i })).toHaveLength(2)
  })

  it('gone 404: reports the refusal upward', async () => {
    withDeleteHandler(columnA.id, () =>
      problem(404, { code: 'boards.not_available', title: 'That column no longer exists.' }),
    )
    const onRefused = vi.fn()
    renderRow(aBoard({ columns: [columnA, columnB], cards: [] }), onRefused)

    const deleteButtons = screen.getAllByRole('button', { name: 'Delete column' })
    await userEvent.click(deleteButtons[0])

    await waitFor(() => expect(onRefused).toHaveBeenCalled())
  })

  it('success: removes the column, keeping the others in relative order (AC-07)', async () => {
    withDeleteHandler(columnA.id, () =>
      jsonResponse(200, {
        column_layout_version: 2,
        columns: [
          { ...columnB, position: 0 },
          { ...columnC, position: 1 },
        ],
      }),
    )
    renderRow(aBoard({ columns: [columnA, columnB, columnC], cards: [] }))

    const deleteButtons = screen.getAllByRole('button', { name: 'Delete column' })
    await userEvent.click(deleteButtons[0])

    await waitFor(() =>
      expect(screen.queryByRole('button', { name: 'Move column To do' })).not.toBeInTheDocument(),
    )
    const remaining = screen.getAllByRole('button', { name: /^move column/i })
    expect(remaining.map((button) => button.getAttribute('aria-label'))).toEqual([
      'Move column Doing',
      'Move column Done',
    ])
  })
})

// ---- column drag: pointer and keyboard (AC-06, AC-24) ---------------------------------------------

/**
 * dnd-kit measures every droppable/draggable node with `getBoundingClientRect`. jsdom returns all
 * zeros for it, which collapses every column onto the same point and makes collision detection
 * undecidable. This stub places each column side by side along the x-axis, in DOM order, keyed off
 * the one thing the spec already requires — the handle's `aria-label «Move column <name>»` — so it
 * needs no markup this task does not already call for.
 */
function stubColumnRects() {
  const width = 300
  Object.defineProperty(Element.prototype, 'getBoundingClientRect', {
    configurable: true,
    value: function (this: Element) {
      const handles = Array.from(document.querySelectorAll('[aria-label^="Move column"]'))
      const index = handles.findIndex(
        (handle) => this === handle || this.contains(handle) || handle.contains(this),
      )
      const left = index >= 0 ? index * width : 0
      return {
        width,
        height: 200,
        top: 0,
        bottom: 200,
        left,
        right: left + width,
        x: left,
        y: 0,
        toJSON: () => ({}),
      } as DOMRect
    },
  })
}

function dragHandleFromTo(handle: HTMLElement, fromX: number, toX: number) {
  fireEvent.pointerDown(handle, { pointerId: 1, clientX: fromX, clientY: 100, isPrimary: true, button: 0 })
  fireEvent.pointerMove(document, { pointerId: 1, clientX: toX, clientY: 100, isPrimary: true, button: 0 })
  fireEvent.pointerUp(document, { pointerId: 1 })
}

describe('Column drag', () => {
  beforeEach(() => {
    stubColumnRects()
  })

  function withMoveHandler(columnId: string, answer: () => Response | Promise<Response>) {
    changeHandler = (url, init) => {
      if (url.endsWith(`${columnPath(columnId)}/position`) && init?.method === 'PUT') {
        return Promise.resolve(answer())
      }
      return undefined
    }
  }

  it('dropped at its own index sends no request', async () => {
    renderRow(aBoard({ columns: [columnA, columnB, columnC], cards: [] }))
    const handle = screen.getByRole('button', { name: 'Move column To do' })

    dragHandleFromTo(handle, 150, 150)

    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('pointer drop moves the column, sending its new position and the layout version last seen (AC-06)', async () => {
    withMoveHandler(columnA.id, () =>
      jsonResponse(200, {
        column_layout_version: 2,
        columns: [
          { ...columnB, position: 0 },
          { ...columnC, position: 1 },
          { ...columnA, position: 2 },
        ],
      }),
    )
    renderRow(aBoard({ columns: [columnA, columnB, columnC], cards: [] }))
    const handle = screen.getByRole('button', { name: 'Move column To do' })

    dragHandleFromTo(handle, 150, 750)

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([url]) => String(url).endsWith(`${columnPath(columnA.id)}/position`)),
      ).toBe(true),
    )
    const call = fetchMock.mock.calls.find(([url]) =>
      String(url).endsWith(`${columnPath(columnA.id)}/position`),
    )
    const init = call?.[1] as RequestInit
    const body = JSON.parse(String(init.body)) as { position: number; column_layout_version: number }
    expect(body.column_layout_version).toBe(1)

    const order = await screen.findAllByRole('button', { name: /^move column/i })
    expect(order.map((button) => button.getAttribute('aria-label'))).toEqual([
      'Move column Doing',
      'Move column Done',
      'Move column To do',
    ])
  })

  it('further drags are paused while a move is pending', async () => {
    withMoveHandler(columnA.id, () => new Promise(() => {}))
    renderRow(aBoard({ columns: [columnA, columnB, columnC], cards: [] }))
    const firstHandle = screen.getByRole('button', { name: 'Move column To do' })

    dragHandleFromTo(firstHandle, 150, 750)
    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    fetchMock.mockClear()

    const secondHandle = screen.getByRole('button', { name: 'Move column Done' })
    dragHandleFromTo(secondHandle, 750, 150)

    await new Promise((resolve) => setTimeout(resolve, 0))
    expect(fetchMock).not.toHaveBeenCalled()
  })

  it('keyboard move (Space, arrow, Space) sends the same request a pointer drop would', async () => {
    withMoveHandler(columnA.id, () =>
      jsonResponse(200, {
        column_layout_version: 2,
        columns: [
          { ...columnB, position: 0 },
          { ...columnA, position: 1 },
          { ...columnC, position: 2 },
        ],
      }),
    )
    renderRow(aBoard({ columns: [columnA, columnB, columnC], cards: [] }))
    const handle = screen.getByRole('button', { name: 'Move column To do' })
    handle.focus()

    fireEvent.keyDown(handle, { code: 'Space' })
    // dnd-kit's KeyboardSensor attaches its keydown listener in a setTimeout(0) once the drag
    // starts, so the following keys must land on a later tick or it never sees them.
    await new Promise((resolve) => setTimeout(resolve, 0))
    fireEvent.keyDown(document, { code: 'ArrowRight' })
    fireEvent.keyDown(document, { code: 'Space' })

    await waitFor(() =>
      expect(
        fetchMock.mock.calls.some(([url]) => String(url).endsWith(`${columnPath(columnA.id)}/position`)),
      ).toBe(true),
    )
  })

  it('stale 409 columns_changed: patches the layout and shows the changed-columns notice (AC-24)', async () => {
    withMoveHandler(columnA.id, () =>
      problem(409, {
        code: 'boards.columns_changed',
        title: 'The columns changed.',
        detail: 'The columns changed since you last saw them.',
        current_layout: {
          column_layout_version: 3,
          columns: [
            { ...columnC, position: 0 },
            { ...columnA, position: 1 },
            { ...columnB, position: 2 },
          ],
        },
      }),
    )
    renderRow(aBoard({ columns: [columnA, columnB, columnC], cards: [] }))
    const handle = screen.getByRole('button', { name: 'Move column To do' })

    dragHandleFromTo(handle, 150, 750)

    expect(await screen.findByText(/the columns changed since you last saw them/i)).toBeInTheDocument()
    const order = screen.getAllByRole('button', { name: /^move column/i })
    expect(order.map((button) => button.getAttribute('aria-label'))).toEqual([
      'Move column Done',
      'Move column To do',
      'Move column Doing',
    ])
  })

  it('gone 404: reports the refusal upward', async () => {
    withMoveHandler(columnA.id, () =>
      problem(404, { code: 'boards.not_available', title: 'That column no longer exists.' }),
    )
    const onRefused = vi.fn()
    renderRow(aBoard({ columns: [columnA, columnB, columnC], cards: [] }), onRefused)
    const handle = screen.getByRole('button', { name: 'Move column To do' })

    dragHandleFromTo(handle, 150, 750)

    await waitFor(() => expect(onRefused).toHaveBeenCalled())
  })
})
