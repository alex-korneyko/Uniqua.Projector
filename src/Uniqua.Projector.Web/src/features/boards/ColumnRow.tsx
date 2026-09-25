import type { Board, CardSummary } from '@/api/boards'
import { BoardColumn } from '@/features/boards/BoardColumn'
import type { KeptDraft } from '@/features/boards/draftStore'

export interface ColumnRowProps {
  board: Board
  onRefused: (error: unknown, kept: KeptDraft) => void
  onOpenCard?: (cardId: string) => void
}

/**
 * The board's columns side by side, in `position` order, each with its cards in `position` order.
 * The row scrolls sideways on its own, so twenty columns on a narrow screen never widen the page.
 * T21 adds the add-column slot at the end and the drag.
 */
export function ColumnRow({ board, onRefused, onOpenCard }: ColumnRowProps) {
  const columns = [...board.columns].sort((a, b) => a.position - b.position)
  const cardsByColumn = groupByColumn(board.cards)

  return (
    <div className="flex min-w-0 items-start gap-4 overflow-x-auto pb-2">
      {columns.map((column) => (
        <BoardColumn
          key={column.id}
          boardId={board.id}
          column={column}
          cards={cardsByColumn.get(column.id) ?? []}
          onRefused={onRefused}
          onOpenCard={onOpenCard}
        />
      ))}
    </div>
  )
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
