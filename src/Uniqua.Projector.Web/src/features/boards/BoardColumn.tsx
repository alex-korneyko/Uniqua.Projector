import type { CardSummary, Column } from '@/api/boards'
import { Button } from '@/components/ui/button'
import { AddCardForm } from '@/features/boards/AddCardForm'
import { PlainText } from '@/features/boards/PlainText'
import type { KeptDraft } from '@/features/boards/draftStore'

export interface BoardColumnProps {
  boardId: string
  column: Column
  /** This column's cards, already in `position` order. */
  cards: CardSummary[]
  onRefused: (error: unknown, kept: KeptDraft) => void
  /** A card tile pressed → SCR-05, wired by T22. */
  onOpenCard?: (cardId: string) => void
}

/**
 * One column: its name, its card tiles and the add-card form. T21 adds the header's drag handle,
 * rename and delete. A tile shows the title only — descriptions are never shown on the board
 * (ADR 0018) — and wraps it rather than cutting it off.
 */
export function BoardColumn({ boardId, column, cards, onRefused, onOpenCard }: BoardColumnProps) {
  return (
    <section className="bg-muted/40 flex w-72 shrink-0 flex-col gap-3 rounded-lg border p-3">
      <h2 className="min-w-0 text-sm font-semibold">
        <PlainText text={column.name} />
      </h2>

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
