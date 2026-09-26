import {
  DndContext,
  KeyboardSensor,
  PointerSensor,
  closestCenter,
  useSensor,
  useSensors,
  type Announcements,
  type DragEndEvent,
  type UniqueIdentifier,
} from '@dnd-kit/core'
import {
  SortableContext,
  arrayMove,
  horizontalListSortingStrategy,
  sortableKeyboardCoordinates,
} from '@dnd-kit/sortable'
import { useQueryClient, type QueryClient } from '@tanstack/react-query'

import {
  boardQueryKey,
  moveColumn,
  patchBoard,
  type Board,
  type CardSummary,
  type Column,
  type ColumnLayout,
  type MoveColumnRequest,
} from '@/api/boards'
import { AddColumnForm } from '@/features/boards/AddColumnForm'
import { BoardColumn } from '@/features/boards/BoardColumn'
import { describeBoardRefusal } from '@/features/boards/boardRefusals'
import type { KeptDraft } from '@/features/boards/draftStore'
import { useBoardChange } from '@/features/boards/useBoardChange'

/** The item a refused column move is reported under; nothing was typed, so nothing is kept. */
export const moveColumnItem = 'move_column'

export interface ColumnRowProps {
  board: Board
  onRefused: (error: unknown, kept: KeptDraft) => void
  onOpenCard?: (cardId: string) => void
}

interface MoveVariables {
  columnId: string
  body: MoveColumnRequest
  /** The columns as they were before the optimistic reorder, restored if the move is refused. */
  before: Column[]
}

/**
 * The board's columns side by side, in `position` order, each with its cards in `position` order,
 * then the add-column slot. The row scrolls sideways on its own, so twenty columns on a narrow
 * screen never widen the page.
 *
 * A column is moved by its handle, with the pointer or the keyboard (Space/Enter, arrows,
 * Space/Enter). The drop is shown at once through the cached board and sent with the
 * `column_layout_version` last seen; further drags wait for the answer. A refusal restores the
 * order it replaced, then a stale one is patched to `current_layout` and named in the notice region
 * above the row (AC-24) — a drag leaves no form behind to show it in.
 */
export function ColumnRow({ board, onRefused, onOpenCard }: ColumnRowProps) {
  const queryClient = useQueryClient()
  const columns = [...board.columns].sort((a, b) => a.position - b.position)
  const cardsByColumn = groupByColumn(board.cards)

  const sensors = useSensors(
    useSensor(PointerSensor),
    useSensor(KeyboardSensor, { coordinateGetter: sortableKeyboardCoordinates }),
  )

  const move = useBoardChange<MoveVariables, ColumnLayout>({
    boardId: board.id,
    mutationFn: async ({ columnId, body, before }) => {
      try {
        return await moveColumn(board.id, columnId, body)
      } catch (error) {
        setCachedColumns(queryClient, board.id, before)
        onRefused(error, { boardId: board.id, item: moveColumnItem, fields: {} })
        throw error
      }
    },
    applyAccepted: (cached, layout) => patchBoard(cached, { kind: 'layout-changed', layout }),
  })

  const onDragEnd = ({ active, over }: DragEndEvent) => {
    if (over === null || active.id === over.id || move.isPending) {
      return
    }

    const from = columns.findIndex((column) => column.id === active.id)
    const to = columns.findIndex((column) => column.id === over.id)
    if (from < 0 || to < 0) {
      return
    }

    const reordered = arrayMove(columns, from, to).map((column, position) => ({ ...column, position }))
    setCachedColumns(queryClient, board.id, reordered)
    move.change({
      columnId: columns[from].id,
      body: { position: to, column_layout_version: board.column_layout_version },
      before: board.columns,
    })
  }

  const notice =
    move.error !== undefined ? describeBoardRefusal(move.error, move.resolvedAs, 'column') : undefined

  return (
    <div className="flex min-w-0 flex-col gap-2">
      {notice !== undefined && (
        <p role="alert" className="text-destructive text-sm">
          {notice}
        </p>
      )}

      <div className="flex min-w-0 items-start gap-4 overflow-x-auto pb-2">
        <DndContext
          sensors={sensors}
          collisionDetection={closestCenter}
          onDragEnd={onDragEnd}
          accessibility={{ announcements: announcementsFor(columns) }}
        >
          <SortableContext
            items={columns.map((column) => column.id)}
            strategy={horizontalListSortingStrategy}
          >
            {columns.map((column) => (
              <BoardColumn
                key={column.id}
                boardId={board.id}
                column={column}
                cards={cardsByColumn.get(column.id) ?? []}
                onRefused={onRefused}
                onOpenCard={onOpenCard}
                dragDisabled={move.isPending}
              />
            ))}
          </SortableContext>
        </DndContext>

        <AddColumnForm boardId={board.id} onRefused={onRefused} />
      </div>
    </div>
  )
}

function setCachedColumns(queryClient: QueryClient, boardId: string, columns: Column[]): void {
  queryClient.setQueryData<Board>(boardQueryKey(boardId), (cached) =>
    cached === undefined ? undefined : { ...cached, columns },
  )
}

/** Screen-reader announcements that name the columns rather than their ids. */
function announcementsFor(columns: Column[]): Announcements {
  const nameOf = (id: UniqueIdentifier | undefined) =>
    columns.find((column) => column.id === id)?.name ?? ''
  const placeOf = (id: UniqueIdentifier | undefined) =>
    columns.findIndex((column) => column.id === id) + 1

  return {
    onDragStart: ({ active }) => `Picked up column ${nameOf(active.id)}.`,
    onDragOver: ({ active, over }) =>
      over === null
        ? `Column ${nameOf(active.id)} is no longer over a place.`
        : `Column ${nameOf(active.id)} is over place ${placeOf(over.id)} of ${columns.length}.`,
    onDragEnd: ({ active, over }) =>
      over === null
        ? `Column ${nameOf(active.id)} was dropped.`
        : `Column ${nameOf(active.id)} was dropped at place ${placeOf(over.id)} of ${columns.length}.`,
    onDragCancel: ({ active }) => `Moving column ${nameOf(active.id)} was cancelled.`,
  }
}

function groupByColumn(cards: CardSummary[]): Map<string, CardSummary[]> {
  const grouped = new Map<string, CardSummary[]>()

  for (const card of [...cards].sort((a, b) => a.position - b.position)) {
    const column = grouped.get(card.column_id)
    if (column === undefined) {
      grouped.set(card.column_id, [card])
    } else {
      column.push(card)
    }
  }

  return grouped
}
