import { QueryClient, QueryClientProvider } from '@tanstack/react-query'
import { render, screen, waitFor } from '@testing-library/react'
import userEvent from '@testing-library/user-event'
import type { ComponentProps } from 'react'
import { MemoryRouter, Route, Routes, useParams } from 'react-router'
import { afterEach, beforeEach, describe, expect, it, vi } from 'vitest'

import { boardQueryKey, type Board } from '@/api/boards'
import { CreateBoardDialog } from '@/features/boards/CreateBoardDialog'
import { takeFor } from '@/features/boards/draftStore'

/**
 * T19 / SCR-03 — the create-board dialog, in every state screens.md enumerates (AC-01, AC-02,
 * AC-03).
 */

const accountId = '018f3a2b-7c4d-7e91-a0b2-3c4d5e6f7a8b'

const createdBoard: Board = {
  id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a8b',
  name: 'Test board',
  is_owner: true,
  column_layout_version: 1,
  columns: [
    { id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a01', name: 'To do', position: 0, name_version: 1 },
    { id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a02', name: 'In progress', position: 1, name_version: 1 },
    { id: '0192f3a0-7c4d-7e91-a0b2-3c4d5e6f7a03', name: 'Done', position: 2, name_version: 1 },
  ],
  cards: [],
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

function problem(status: number, body: Record<string, unknown>, retryAfterSeconds?: number) {
  return {
    ok: false,
    status,
    json: async () => ({ ...body, retry_after_seconds: retryAfterSeconds }),
    text: async () => JSON.stringify(body),
    headers: new Headers({ 'content-type': 'application/problem+json' }),
  } as unknown as Response
}

const fetchMock = vi.fn()

beforeEach(() => {
  vi.stubGlobal('fetch', fetchMock)
  fetchMock.mockReset()
  document.cookie = 'XSRF-TOKEN=a-token'
  sessionStorage.clear()
})

afterEach(() => {
  vi.unstubAllGlobals()
})

function OpenedBoardSpy() {
  const { boardId } = useParams()
  return <div data-testid="opened-board">{boardId}</div>
}

function renderDialog(props: Partial<ComponentProps<typeof CreateBoardDialog>> = {}) {
  // No `gcTime: 0` here: each test gets its own client instance, so nothing leaks between tests,
  // and the "success" test below reads the board query this component seeds straight after the
  // 201 — a `gcTime` of 0 would drop it the instant it becomes unobserved.
  const client = new QueryClient({ defaultOptions: { queries: { retry: false } } })
  const onOpenChange = vi.fn()

  render(
    <QueryClientProvider client={client}>
      <MemoryRouter initialEntries={['/']}>
        <Routes>
          <Route
            path="/"
            element={
              <CreateBoardDialog
                open
                onOpenChange={onOpenChange}
                accountId={accountId}
                {...props}
              />
            }
          />
          <Route path="/boards/:boardId" element={<OpenedBoardSpy />} />
        </Routes>
      </MemoryRouter>
    </QueryClientProvider>,
  )

  return { client, onOpenChange }
}

function nameField() {
  return screen.getByLabelText(/board name/i)
}

async function fillAndSubmit(name: string) {
  await userEvent.clear(nameField())
  if (name.length > 0) {
    await userEvent.type(nameField(), name)
  }
  await userEvent.click(screen.getByRole('button', { name: /^create$/i }))
}

describe('the default state', () => {
  it('offers the name field, a hint, and no refusal', () => {
    renderDialog()

    expect(nameField()).toHaveValue('')
    expect(screen.getByText(/between 1 and 100 characters/i)).toBeInTheDocument()
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('autofocuses the name field', () => {
    renderDialog()

    expect(nameField()).toHaveFocus()
  })

  it('fills the field from a kept name (AC-28 apply again)', () => {
    renderDialog({ initialName: 'Kept board name' })

    expect(nameField()).toHaveValue('Kept board name')
  })
})

describe('while the request is in flight (pending)', () => {
  it('shows a busy label and disables the button', async () => {
    fetchMock.mockReturnValue(new Promise(() => {}))

    renderDialog()
    await fillAndSubmit('A new board')

    await waitFor(() => expect(screen.getByRole('button', { name: /creating/i })).toBeDisabled())
  })
})

describe('validation (AC-02)', () => {
  it('refuses a name of only spaces before any request is sent', async () => {
    renderDialog()
    await fillAndSubmit('    ')

    expect(await screen.findByRole('alert')).toHaveTextContent(/between 1 and 100 characters/i)
    expect(fetchMock).not.toHaveBeenCalled()
    expect(nameField()).toHaveFocus()
  })

  it('accepts 100 emoji, counted by code point rather than UTF-16 length', async () => {
    fetchMock.mockResolvedValue(jsonResponse(201, createdBoard))
    const hundredEmoji = '😀'.repeat(100)

    renderDialog()
    // 100 emoji is 200 UTF-16 units; typing them key-by-key is slow enough to blow the test timeout
    // and leaves stray keystrokes firing into whatever renders next. Paste delivers the same value
    // change without simulating each keystroke.
    await userEvent.click(nameField())
    await userEvent.paste(hundredEmoji)
    await userEvent.click(screen.getByRole('button', { name: /^create$/i }))

    await waitFor(() => expect(fetchMock).toHaveBeenCalled())
    expect(screen.queryByRole('alert')).not.toBeInTheDocument()
  })

  it('shows the server refusal, keeps what was typed, and returns focus to the field', async () => {
    fetchMock.mockResolvedValue(
      problem(400, {
        code: 'boards.board_name_invalid',
        title: 'The board name is not usable',
        detail: 'A board name must be between 1 and 100 characters.',
      }),
    )

    renderDialog()
    await fillAndSubmit('x'.repeat(101))

    expect(await screen.findByRole('alert')).toHaveTextContent(/between 1 and 100 characters/i)
    expect(nameField()).toHaveValue('x'.repeat(101))
    await waitFor(() => expect(nameField()).toHaveFocus())
  })
})

describe('the limit (AC-03)', () => {
  it('disables Create, only Cancel closes the dialog, until it is reopened', async () => {
    fetchMock.mockResolvedValue(
      problem(409, {
        code: 'boards.owned_board_limit_reached',
        title: 'No more boards can be created',
        detail: 'An account can own at most 50 boards.',
      }),
    )

    renderDialog()
    await fillAndSubmit('One too many')

    expect(await screen.findByRole('alert')).toHaveTextContent(/at most 50 boards/i)
    expect(screen.getByRole('button', { name: /^create$/i })).toBeDisabled()
    expect(screen.getByRole('button', { name: /cancel/i })).toBeEnabled()
  })
})

describe('rate-limited', () => {
  it('shows the refusal with a wait, and keeps the name', async () => {
    fetchMock.mockResolvedValue(
      problem(
        429,
        {
          code: 'boards.change_rate_limited',
          title: 'Changes are temporarily limited',
          detail: 'You have made many changes in the past minute. You can continue shortly.',
        },
        12,
      ),
    )

    renderDialog()
    await fillAndSubmit('A board')

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(nameField()).toHaveValue('A board')
  })
})

describe('busy (contended)', () => {
  it('shows a refusal and keeps the name', async () => {
    fetchMock.mockResolvedValue(
      problem(
        503,
        {
          code: 'boards.contended',
          title: 'The board is busy',
          detail: 'The change could not be applied. Please try again.',
        },
        1,
      ),
    )

    renderDialog()
    await fillAndSubmit('A board')

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(nameField()).toHaveValue('A board')
  })
})

describe('session-ended (AC-28)', () => {
  it('keeps the typed name under the account id, for SCR-02 to offer back', async () => {
    fetchMock.mockResolvedValue(
      problem(401, { code: 'accounts.session_not_recognised', detail: 'Sign in to continue.' }),
    )

    renderDialog()
    await fillAndSubmit('Kept across sign-in')

    await waitFor(() => expect(takeFor(accountId)?.fields.name).toBe('Kept across sign-in'))
  })
})

describe('error (an answer the contract does not name, or none at all)', () => {
  it('shows one plain message and keeps the name', async () => {
    fetchMock.mockRejectedValue(new TypeError('Failed to fetch'))

    renderDialog()
    await fillAndSubmit('A board')

    expect(await screen.findByRole('alert')).toBeInTheDocument()
    expect(nameField()).toHaveValue('A board')
  })
})

describe('success (AC-01)', () => {
  it('closes the dialog, opens the new board, and seeds its cache from the 201 body', async () => {
    fetchMock.mockResolvedValue(jsonResponse(201, createdBoard))

    const { client, onOpenChange } = renderDialog()
    await fillAndSubmit('Test board')

    await waitFor(() => expect(onOpenChange).toHaveBeenCalledWith(false))
    expect(await screen.findByTestId('opened-board')).toHaveTextContent(createdBoard.id)
    expect(client.getQueryData(boardQueryKey(createdBoard.id))).toEqual(createdBoard)
  })
})
