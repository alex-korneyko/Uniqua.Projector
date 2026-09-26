import { useState, type FormEvent } from 'react'

import { ApiError } from '@/api/accounts'
import { addCard, patchBoard, type AddCardRequest, type CardSummary } from '@/api/boards'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import { describeBoardRefusal } from '@/features/boards/boardRefusals'
import type { KeptDraft } from '@/features/boards/draftStore'
import { useBoardChange } from '@/features/boards/useBoardChange'

/** The item a kept add-card is filed under in `draftStore`, offered back by SCR-04. */
export const addCardItem = 'add_card'

export interface AddCardFormProps {
  boardId: string
  columnId: string
  /** Every refusal, with what was typed, for the screen to act on (kept text, re-reads). */
  onRefused: (error: unknown, kept: KeptDraft) => void
}

const titleInvalid = 'boards.card_title_invalid'
const descriptionInvalid = 'boards.card_description_invalid'

/**
 * The add-card form at the foot of a column (AC-12). Closed it is «+ Add card»; open it is a title
 * and an optional description. A refusal is shown under the field it names, or under the form, and
 * both fields keep their text (AC-14, AC-15). An accepted card lands last in its column through the
 * cache patch, and the form clears and stays open for the next one.
 */
export function AddCardForm({ boardId, columnId, onRefused }: AddCardFormProps) {
  const [open, setOpen] = useState(false)
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')

  const add = useBoardChange<AddCardRequest, CardSummary>({
    boardId,
    mutationFn: async (body) => {
      try {
        const card = await addCard(boardId, body)
        setTitle('')
        setDescription('')
        return card
      } catch (error) {
        onRefused(error, keptAddCard(boardId, body))
        throw error
      }
    },
    applyAccepted: (board, card) => patchBoard(board, { kind: 'card-added', card }),
  })

  if (!open) {
    return (
      <Button variant="ghost" className="w-full justify-start" onClick={() => setOpen(true)}>
        + Add card
      </Button>
    )
  }

  const refusal = add.error === undefined ? undefined : describeBoardRefusal(add.error, add.resolvedAs, 'column')
  const refusedCode = add.error instanceof ApiError ? add.error.code : undefined
  const titleRefusal = refusedCode === titleInvalid ? refusal : undefined
  const descriptionRefusal = refusedCode === descriptionInvalid ? refusal : undefined
  const formRefusal = titleRefusal === undefined && descriptionRefusal === undefined ? refusal : undefined

  const ids = {
    title: `add-card-title-${columnId}`,
    description: `add-card-description-${columnId}`,
  }

  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    add.change({
      column_id: columnId,
      title,
      description: description.length > 0 ? description : undefined,
    })
  }

  const close = () => {
    setTitle('')
    setDescription('')
    setOpen(false)
  }

  return (
    <form noValidate className="flex flex-col gap-3" onSubmit={onSubmit}>
      <div className="flex flex-col gap-2">
        <Label htmlFor={ids.title}>Title</Label>
        <Input
          id={ids.title}
          autoFocus
          autoComplete="off"
          aria-describedby={`${ids.title}-hint`}
          aria-invalid={titleRefusal !== undefined ? true : undefined}
          value={title}
          onChange={(event) => setTitle(event.target.value)}
        />
        <p id={`${ids.title}-hint`} className="text-muted-foreground text-xs">
          Between 1 and 150 characters.
        </p>
        <RefusalLine text={titleRefusal} />
      </div>

      <div className="flex flex-col gap-2">
        <Label htmlFor={ids.description}>Description (optional)</Label>
        <Textarea
          id={ids.description}
          aria-describedby={`${ids.description}-hint`}
          aria-invalid={descriptionRefusal !== undefined ? true : undefined}
          value={description}
          onChange={(event) => setDescription(event.target.value)}
        />
        <p id={`${ids.description}-hint`} className="text-muted-foreground text-xs">
          At most 10,000 characters.
        </p>
        <RefusalLine text={descriptionRefusal} />
      </div>

      <RefusalLine text={formRefusal} />

      <div className="flex items-center justify-end gap-2">
        {add.isPending && (
          <span role="status" aria-live="polite" className="text-muted-foreground text-xs">
            Adding…
          </span>
        )}
        <Button type="button" variant="ghost" size="sm" onClick={close}>
          Cancel
        </Button>
        <Button type="submit" size="sm" disabled={add.isPending}>
          Add
        </Button>
      </div>
    </form>
  )
}

function RefusalLine({ text }: { text: string | undefined }) {
  if (text === undefined) {
    return null
  }

  return (
    <p role="alert" className="text-destructive text-sm">
      {text}
    </p>
  )
}

function keptAddCard(boardId: string, body: AddCardRequest): KeptDraft {
  return {
    boardId,
    item: addCardItem,
    fields: { column_id: body.column_id, title: body.title, description: body.description ?? '' },
  }
}
