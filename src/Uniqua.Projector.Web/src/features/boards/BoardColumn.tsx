import { useSortable } from '@dnd-kit/sortable'

import type { CardSummary, Column } from '@/api/boards'
import { Button } from '@/components/ui/button'
import { AddCardForm } from '@/features/boards/AddCardForm'
import { ColumnHeader } from '@/features/boards/ColumnHeader'
import { PlainText } from '@/features/boards/PlainText'
import type { KeptDraft } from '@/features/boards/draftStore'
import { cn } from '@/lib/utils'

export interface BoardColumnProps {
  boardId: string
  column: Column
  /** This column's cards, already in `position` order. */
  cards: CardSummary[]
  onRefused: (error: unknown, kept: KeptDraft) => void
  /** A card tile pressed → SCR-05, wired by T22. */
  onOpenCard?: (cardId: string) => void
  /** Set while a column move is pending: further drags wait for its answer. */
  dragDisabled?: boolean
}

/**
 * One column, a dnd-kit sortable item: its header (drag handle, name, rename, delete), its card
 * tiles and the add-card form. A tile shows the title only — descriptions are never shown on the
 * board (ADR 0018) — and wraps it rather than cutting it off. While dragged the column is lifted and
 * the column it would land on is outlined, both with utility classes.
 */
export function BoardColumn({
  boardId,
  column,
  cards,
  onRefused,
  onOpenCard,
  dragDisabled = false,
}: BoardColumnProps) {
  const { setNodeRef, setActivatorNodeRef, attributes, listeners, isDragging, isOver } = useSortable({
    id: column.id,
    disabled: dragDisabled,
  })

  return (
    <section
      ref={setNodeRef}
      className={cn(
        'bg-muted/40 flex w-72 shrink-0 flex-col gap-3 rounded-lg border p-3',
        isDragging && 'opacity-60 shadow-lg',
        isOver && !isDragging && 'ring-primary ring-2',
      )}
    >
      <ColumnHeader
        boardId={boardId}
        column={column}
        onRefused={onRefused}
        handle={{ setActivatorNodeRef, attributes, listeners }}
      />

      {cards.length > 0 && (
        <ul className="flex flex-col gap-2">
          {cards.map((card) => (
            <li key={card.id}>
              <Button
                variant="outline"
                className="h-auto w-full justify-start py-2 text-left whitespace-normal"
                onClick={() => onOpenCard?.(card.id)}
              >
                <PlainText text={card.title} className="min-w-0" />
              </Button>
            </li>
          ))}
        </ul>
      )}

      <AddCardForm boardId={boardId} columnId={column.id} onRefused={onRefused} />
    </section>
  )
}
