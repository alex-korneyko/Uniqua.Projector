/**
 * The boards transport, written against `docs/features/boards-columns-cards/contracts/openapi.yaml`.
 *
 * T17. One function per operation, over the shared `request<T>()` from `api/accounts.ts`, so the
 * session cookie and the antiforgery header are attached exactly the one way the client attaches
 * them anywhere else. Every state-changing call sends `X-XSRF-TOKEN`, and every refusal arrives as
 * the shared `ApiError`, whose `current*` members carry the state a stale refusal returns
 * (ADR 0016).
 */

import { ApiError, request } from '@/api/accounts'

export { ApiError }

// ---- stored text, as returned ----------------------------------------------------------------

export interface Column {
  id: string
  name: string
  position: number
  name_version: number
}

export interface ColumnLayout {
  column_layout_version: number
  columns: Column[]
}

export interface AddedColumn {
  column: Column
  column_layout_version: number
}

export interface CardSummary {
  id: string
  column_id: string
  position: number
  title: string
  content_version: number
}

export interface Card {
  id: string
  column_id: string
  position: number
  title: string
  description: string
  content_version: number
}

export interface Board {
  id: string
  name: string
  is_owner: boolean
  column_layout_version: number
  columns: Column[]
  cards: CardSummary[]
}

export interface BoardName {
  id: string
  name: string
}

export interface BoardListEntry {
  id: string
  name: string
  created_at: string
  is_owner: boolean
}

export interface BoardListPage {
  items: BoardListEntry[]
  has_next: boolean
  has_prev: boolean
  next_cursor: string | null
  prev_cursor: string | null
}

// ---- requests -----------------------------------------------------------------------------------

export interface ListMyBoardsParams {
  after?: string
  before?: string
  limit?: number
}

export interface CreateBoardRequest {
  name: string
}

export interface RenameBoardRequest {
  name: string
}

export interface DeleteBoardRequest {
  confirm_name: string
}

export interface AddColumnRequest {
  name: string
}

export interface RenameColumnRequest {
  name: string
  name_version: number
}

export interface MoveColumnRequest {
  position: number
  column_layout_version: number
}

export interface DeleteColumnRequest {
  name_version: number
}

export interface AddCardRequest {
  column_id: string
  title: string
  description?: string
}

export interface EditCardRequest {
  title?: string
  description?: string
  content_version: number
}

export interface DeleteCardRequest {
  content_version: number
}

// ---- operations ---------------------------------------------------------------------------------

const boardsPath = '/api/v1/boards'
const boardPath = (boardId: string) => `${boardsPath}/${encodeURIComponent(boardId)}`
const columnPath = (boardId: string, columnId: string) =>
  `${boardPath(boardId)}/columns/${encodeURIComponent(columnId)}`
const cardPath = (boardId: string, cardId: string) =>
  `${boardPath(boardId)}/cards/${encodeURIComponent(cardId)}`

export function listMyBoards(params: ListMyBoardsParams = {}): Promise<BoardListPage> {
  const query = new URLSearchParams()
  if (params.after !== undefined) query.set('after', params.after)
  if (params.before !== undefined) query.set('before', params.before)
  if (params.limit !== undefined) query.set('limit', String(params.limit))

  const search = query.toString()
  return request<BoardListPage>(search.length > 0 ? `${boardsPath}?${search}` : boardsPath)
}

export function createBoard(body: CreateBoardRequest): Promise<Board> {
  return request<Board>(boardsPath, { method: 'POST', body })
}

export function openBoard(boardId: string): Promise<Board> {
  return request<Board>(boardPath(boardId))
}

export function renameBoard(boardId: string, body: RenameBoardRequest): Promise<BoardName> {
  return request<BoardName>(boardPath(boardId), { method: 'PATCH', body })
}

export async function deleteBoard(boardId: string, body: DeleteBoardRequest): Promise<void> {
  await request<void>(boardPath(boardId), { method: 'DELETE', body, expectsBody: false })
}

export function addColumn(boardId: string, body: AddColumnRequest): Promise<AddedColumn> {
  return request<AddedColumn>(`${boardPath(boardId)}/columns`, { method: 'POST', body })
}

export function renameColumn(
  boardId: string,
  columnId: string,
  body: RenameColumnRequest,
): Promise<Column> {
  return request<Column>(columnPath(boardId, columnId), { method: 'PATCH', body })
}

export function moveColumn(
  boardId: string,
  columnId: string,
  body: MoveColumnRequest,
): Promise<ColumnLayout> {
  return request<ColumnLayout>(`${columnPath(boardId, columnId)}/position`, { method: 'PUT', body })
}

export function deleteColumn(
  boardId: string,
  columnId: string,
  body: DeleteColumnRequest,
): Promise<ColumnLayout> {
  return request<ColumnLayout>(columnPath(boardId, columnId), { method: 'DELETE', body })
}

export function addCard(boardId: string, body: AddCardRequest): Promise<CardSummary> {
  return request<CardSummary>(`${boardPath(boardId)}/cards`, { method: 'POST', body })
}

export function openCard(boardId: string, cardId: string): Promise<Card> {
  return request<Card>(cardPath(boardId, cardId))
}

export function editCard(boardId: string, cardId: string, body: EditCardRequest): Promise<Card> {
  return request<Card>(cardPath(boardId, cardId), { method: 'PATCH', body })
}

export async function deleteCard(
  boardId: string,
  cardId: string,
  body: DeleteCardRequest,
): Promise<void> {
  await request<void>(cardPath(boardId, cardId), { method: 'DELETE', body, expectsBody: false })
}

// ---- query keys -----------------------------------------------------------------------------------

export const boardsQueryKey = ['boards'] as const
export const boardQueryKey = (boardId: string) => ['board', boardId] as const
export const cardQueryKey = (boardId: string, cardId: string) => ['card', boardId, cardId] as const

// ---- cache patches --------------------------------------------------------------------------------
// Pure functions from a cached value and the server's answer to the next cached value. A change
// reaches the cache only through these, so TanStack Query stays the one copy of the board.

/** An accepted change, as the server answered it. */
export type BoardChange =
  | { kind: 'board-renamed'; board: BoardName }
  | { kind: 'column-added'; added: AddedColumn }
  | { kind: 'column-renamed'; column: Column }
  /** A move or a delete: both answer with the board's whole column set as it now is. */
  | { kind: 'layout-changed'; layout: ColumnLayout }
  | { kind: 'card-added'; card: CardSummary }
  | { kind: 'card-edited'; card: Card }
  | { kind: 'card-deleted'; cardId: string }

/** The `current_*` members a stale refusal carries (ADR 0016), named as the problem document names them. */
export interface CurrentState {
  current_card?: Card
  current_column?: Column
  current_layout?: ColumnLayout
  current_name?: string
}

/** Reads the `current_*` members off a refusal, typed to this contract's schemas. */
export function currentStateOf(error: ApiError): CurrentState {
  return {
    current_card: error.currentCard as Card | undefined,
    current_column: error.currentColumn as Column | undefined,
    current_layout: error.currentLayout as ColumnLayout | undefined,
    current_name: error.currentName as string | undefined,
  }
}

/** Applies an accepted board rename, column add/rename/move/delete or card add/edit/delete onto a cached board. */
export function patchBoard(board: Board, change: BoardChange): Board {
  switch (change.kind) {
    case 'board-renamed':
      return { ...board, name: change.board.name }

    case 'column-added':
      return {
        ...board,
        column_layout_version: change.added.column_layout_version,
        columns: [...board.columns, change.added.column],
      }

    case 'column-renamed':
      return withColumn(board, change.column)

    case 'layout-changed':
      return withLayout(board, change.layout)

    case 'card-added':
      return { ...board, cards: [...board.cards, change.card] }

    case 'card-edited':
      return withCard(board, change.card)

    case 'card-deleted':
      return { ...board, cards: board.cards.filter((card) => card.id !== change.cardId) }
  }
}

/** Applies a `current_name` / `current_layout` / `current_column` / `current_card` payload onto a cached board. */
export function patchBoardFromCurrent(board: Board, current: CurrentState): Board {
  let next = board

  if (current.current_name !== undefined) {
    next = { ...next, name: current.current_name }
  }

  if (current.current_layout !== undefined) {
    next = withLayout(next, current.current_layout)
  }

  if (current.current_column !== undefined) {
    next = withColumn(next, current.current_column)
  }

  if (current.current_card !== undefined) {
    next = withCard(next, current.current_card)
  }

  return next
}

/** Applies an accepted card edit onto a cached card. */
export function patchCard(card: Card, change: Partial<Omit<Card, 'id'>>): Card {
  return { ...card, ...change }
}

function withColumn(board: Board, column: Column): Board {
  return {
    ...board,
    columns: board.columns.map((existing) => (existing.id === column.id ? column : existing)),
  }
}

function withLayout(board: Board, layout: ColumnLayout): Board {
  return { ...board, column_layout_version: layout.column_layout_version, columns: layout.columns }
}

/** Replaces a card's summary on the board from its full text — the board holds summaries only. */
function withCard(board: Board, card: Card): Board {
  const summary: CardSummary = {
    id: card.id,
    column_id: card.column_id,
    position: card.position,
    title: card.title,
    content_version: card.content_version,
  }

  return {
    ...board,
    cards: board.cards.map((existing) => (existing.id === card.id ? summary : existing)),
  }
}
