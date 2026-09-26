import { useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type FormEvent } from 'react'

import { ApiError } from '@/api/accounts'
import {
  boardQueryKey,
  cardQueryKey,
  deleteCard,
  editCard,
  openBoard,
  openCard,
  patchBoard,
  type Board,
  type Card,
  type EditCardRequest,
} from '@/api/boards'
import { Button } from '@/components/ui/button'
import { Card as CardBlock, CardContent } from '@/components/ui/card'
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Textarea } from '@/components/ui/textarea'
import type { KeptTextField } from '@/features/boards/KeptTextNotice'
import { PlainText } from '@/features/boards/PlainText'
import { describeBoardRefusal } from '@/features/boards/boardRefusals'
import { useBoardChange, type BoardChangeResolution } from '@/features/boards/useBoardChange'

export interface CardDetailDialogProps {
  boardId: string
  cardId: string
  /** The title as last known from the board's card summary, shown while the full card loads. */
  summaryTitle: string
  open: boolean
  onOpenChange: (open: boolean) => void
  /**
   * SCR-05 gone (read or save) when the board still answers: the line to show once this dialog has
   * closed, and — after a refused save — what was typed, so it is not lost (AC-18b).
   */
  onCardGone: (message: string, kept?: KeptTextField[]) => void
}

type Step = 'reading' | 'editing' | 'confirming'

const cardChanged = 'boards.card_changed'
const titleInvalid = 'boards.card_title_invalid'
const descriptionInvalid = 'boards.card_description_invalid'
const cardGoneText = 'That card no longer exists.'

/**
 * SCR-05 «Card detail» with SCR-06 «Confirm card deletion» as a step inside the same `Dialog` (one
 * focus trap). The card is read with `openCard`; every change goes through `useBoardChange` with the
 * `content_version` last seen, so a stale refusal patches both the card and its tile from
 * `current_card` (ADR 0018) and the typed text stays in the fields (AC-23). Card text is shown only
 * through `PlainText` (AC-16).
 */
export function CardDetailDialog({
  boardId,
  cardId,
  summaryTitle,
  open,
  onOpenChange,
  onCardGone,
}: CardDetailDialogProps) {
  const queryClient = useQueryClient()

  const [step, setStep] = useState<Step>('reading')
  const [title, setTitle] = useState('')
  const [description, setDescription] = useState('')
  const [editRefusalShown, setEditRefusalShown] = useState(false)
  const [deleteRefusalShown, setDeleteRefusalShown] = useState(false)
  const [readingNotice, setReadingNotice] = useState<string | undefined>(undefined)

  const card = useQuery({
    queryKey: cardQueryKey(boardId, cardId),
    queryFn: async () => {
      try {
        return await openCard(boardId, cardId)
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) {
          // Gone read: only the board's own answer tells a gone card from a gone board apart.
          if (await boardStillAnswers(queryClient, boardId)) {
            onOpenChange(false)
            onCardGone(cardGoneText)
          }
        }
        throw error
      }
    },
    retry: false,
  })

  const edit = useBoardChange<EditCardRequest, Card>({
    boardId,
    mutationFn: async (body) => {
      const saved = await editCard(boardId, cardId, body)
      setStep('reading')
      return saved
    },
    applyAccepted: (board, saved) => patchBoard(board, { kind: 'card-edited', card: saved }),
    acceptedCard: (saved) => saved,
  })

  const remove = useBoardChange<number, void>({
    boardId,
    mutationFn: async (contentVersion) => {
      try {
        await deleteCard(boardId, cardId, { content_version: contentVersion })
      } catch (error) {
        if (error instanceof ApiError && error.code === cardChanged) {
          // SCR-06 stale: back to reading, patched to `current_card`, so the member decides again.
          setReadingNotice(describeBoardRefusal(error))
          setStep('reading')
        }
        throw error
      }
      // Patched before closing, so the board behind the dialog never shows the tile again (AC-18).
      queryClient.setQueryData<Board>(boardQueryKey(boardId), (board) =>
        board === undefined ? undefined : patchBoard(board, { kind: 'card-deleted', cardId }),
      )
      onOpenChange(false)
    },
  })

  useReportGone(edit.error, edit.resolvedAs, () => {
    onOpenChange(false)
    onCardGone(cardGoneText, keptFields(title, description))
  })
  useReportGone(remove.error, remove.resolvedAs, () => {
    onOpenChange(false)
    onCardGone(cardGoneText)
  })

  const current = card.data

  const startEditing = () => {
    if (current === undefined) {
      return
    }
    setTitle(current.title)
    setDescription(current.description)
    setEditRefusalShown(false)
    setReadingNotice(undefined)
    setStep('editing')
  }

  const startConfirming = () => {
    setDeleteRefusalShown(false)
    setReadingNotice(undefined)
    setStep('confirming')
  }

  const backToReading = () => {
    setReadingNotice(undefined)
    setStep('reading')
  }

  const onSave = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    if (current === undefined) {
      return
    }
    setEditRefusalShown(true)
    edit.change({ title, description, content_version: current.content_version })
  }

  const onConfirmDelete = () => {
    if (current !== undefined) {
      setDeleteRefusalShown(true)
      remove.change(current.content_version)
    }
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent aria-describedby={undefined}>
        {current === undefined ? (
          <>
            <DialogHeader>
              <DialogTitle>
                <PlainText text={summaryTitle} />
              </DialogTitle>
            </DialogHeader>
            {card.isError ? (
              <div role="alert" className="flex flex-col items-start gap-3">
                <p className="text-sm font-medium">We could not load this card</p>
                <Button variant="outline" size="sm" onClick={() => void card.refetch()}>
                  Try again
                </Button>
              </div>
            ) : (
              <p role="status" aria-live="polite" className="text-muted-foreground text-sm">
                Loading the card…
              </p>
            )}
          </>
        ) : step === 'editing' ? (
          <CardEditor
            cardId={cardId}
            current={current}
            title={title}
            description={description}
            onTitleChange={setTitle}
            onDescriptionChange={setDescription}
            error={editRefusalShown ? edit.error : undefined}
            resolvedAs={edit.resolvedAs}
            pending={edit.isPending}
            onSubmit={onSave}
            onCancel={backToReading}
          />
        ) : step === 'confirming' ? (
          <>
            <DialogHeader>
              <DialogTitle>Delete this card?</DialogTitle>
            </DialogHeader>
            <div className="flex flex-col gap-2 text-sm">
              <PlainText as="p" text={current.title} className="font-medium" />
              <p>This cannot be undone.</p>
            </div>
            <RefusalLine
              text={
                deleteRefusalShown && remove.error !== undefined && remove.resolvedAs === undefined
                  ? describeBoardRefusal(remove.error)
                  : undefined
              }
            />
            <DialogFooter>
              <Button variant="ghost" onClick={backToReading}>
                Cancel
              </Button>
              <Button variant="destructive" disabled={remove.isPending} onClick={onConfirmDelete}>
                {remove.isPending ? 'Deleting…' : 'Delete card'}
              </Button>
            </DialogFooter>
          </>
        ) : (
          <>
            <DialogHeader>
              <DialogTitle>
                <PlainText text={current.title} />
              </DialogTitle>
            </DialogHeader>
            <div className="max-h-[60vh] overflow-y-auto text-sm">
              <Description text={current.description} />
            </div>
            <RefusalLine text={readingNotice} />
            <DialogFooter className="sm:justify-between">
              <Button variant="ghost" onClick={startConfirming}>
                Delete card
              </Button>
              <Button variant="outline" onClick={startEditing}>
                Edit
              </Button>
            </DialogFooter>
          </>
        )}
      </DialogContent>
    </Dialog>
  )
}

interface CardEditorProps {
  cardId: string
  current: Card
  title: string
  description: string
  onTitleChange: (title: string) => void
  onDescriptionChange: (description: string) => void
  error: unknown
  resolvedAs: BoardChangeResolution
  pending: boolean
  onSubmit: (event: FormEvent<HTMLFormElement>) => void
  onCancel: () => void
}

/** SCR-05 editing: title and description, each refusal under the field it names (AC-14). */
function CardEditor({
  cardId,
  current,
  title,
  description,
  onTitleChange,
  onDescriptionChange,
  error,
  resolvedAs,
  pending,
  onSubmit,
  onCancel,
}: CardEditorProps) {
  // A gone card closes the dialog (`useReportGone`), so it never needs a line here.
  const refusal =
    error === undefined || resolvedAs === 'item-gone' ? undefined : describeBoardRefusal(error, resolvedAs)
  const refusedCode = error instanceof ApiError ? error.code : undefined
  const titleRefusal = refusedCode === titleInvalid ? refusal : undefined
  const descriptionRefusal = refusedCode === descriptionInvalid ? refusal : undefined
  const formRefusal = titleRefusal === undefined && descriptionRefusal === undefined ? refusal : undefined
  const stale = refusedCode === cardChanged

  const ids = {
    title: `card-title-${cardId}`,
    description: `card-description-${cardId}`,
  }

  return (
    <>
      <DialogHeader>
        <DialogTitle>Edit card</DialogTitle>
      </DialogHeader>

      <form noValidate className="flex flex-col gap-4" onSubmit={onSubmit}>
        {stale && (
          <CardBlock>
            <CardContent className="flex flex-col gap-1 pt-4 text-sm">
              <p className="text-muted-foreground text-xs font-medium">Current version</p>
              <PlainText as="p" text={current.title} className="font-medium" />
              <Description text={current.description} />
            </CardContent>
          </CardBlock>
        )}

        <RefusalLine text={formRefusal} />

        <div className="flex flex-col gap-2">
          <Label htmlFor={ids.title}>Title</Label>
          <Input
            id={ids.title}
            autoComplete="off"
            aria-describedby={`${ids.title}-hint`}
            aria-invalid={titleRefusal !== undefined ? true : undefined}
            value={title}
            onChange={(event) => onTitleChange(event.target.value)}
          />
          <p id={`${ids.title}-hint`} className="text-muted-foreground text-xs">
            Between 1 and 150 characters.
          </p>
          <RefusalLine text={titleRefusal} />
        </div>

        <div className="flex flex-col gap-2">
          <Label htmlFor={ids.description}>Description</Label>
          <Textarea
            id={ids.description}
            aria-describedby={`${ids.description}-hint`}
            aria-invalid={descriptionRefusal !== undefined ? true : undefined}
            value={description}
            onChange={(event) => onDescriptionChange(event.target.value)}
          />
          <p id={`${ids.description}-hint`} className="text-muted-foreground text-xs">
            At most 10,000 characters.
          </p>
          <RefusalLine text={descriptionRefusal} />
        </div>

        <DialogFooter>
          <Button type="button" variant="ghost" onClick={onCancel}>
            Cancel
          </Button>
          <Button type="submit" disabled={pending}>
            {pending ? 'Saving…' : 'Save'}
          </Button>
        </DialogFooter>
      </form>
    </>
  )
}

/** The description as typed, or a muted «No description» when there is none. */
function Description({ text }: { text: string }) {
  if (text.length === 0) {
    return <p className="text-muted-foreground">No description</p>
  }

  return <PlainText as="p" text={text} />
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

/**
 * Calls `report` once for each refusal `useBoardChange` resolved as a gone card (AC-18b): the
 * resolution is only known after the change settles, so it is answered here rather than in the
 * change itself.
 */
function useReportGone(error: unknown, resolvedAs: BoardChangeResolution, report: () => void) {
  const reported = useRef<unknown>(undefined)
  const latestReport = useRef(report)

  useEffect(() => {
    latestReport.current = report
  })

  useEffect(() => {
    if (error === undefined || resolvedAs !== 'item-gone' || reported.current === error) {
      return
    }
    reported.current = error
    latestReport.current()
  }, [error, resolvedAs])
}

/**
 * The one re-read after a gone card read (sad.md §6 flow 2). A board that answers refreshes the
 * cache; a board that is gone loses its cached copy, so SCR-04 turns into SCR-08.
 */
async function boardStillAnswers(queryClient: QueryClient, boardId: string): Promise<boolean> {
  const key = boardQueryKey(boardId)

  try {
    await queryClient.fetchQuery({ queryKey: key, queryFn: () => openBoard(boardId), staleTime: 0, retry: false })
    return true
  } catch (error) {
    if (error instanceof ApiError && error.status === 404) {
      queryClient.removeQueries({ queryKey: key })
    }
    return false
  }
}

function keptFields(title: string, description: string): KeptTextField[] {
  const fields: KeptTextField[] = [{ label: 'Card title', value: title }]
  if (description.length > 0) {
    fields.push({ label: 'Description', value: description })
  }
  return fields
}
