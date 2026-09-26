import { useQuery, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router'

import { ApiError } from '@/api/accounts'
import {
  addCard,
  boardQueryKey,
  boardsQueryKey,
  openBoard,
  patchBoard,
  renameBoard,
  type Board,
  type BoardChange,
} from '@/api/boards'
import { Button } from '@/components/ui/button'
import { useSession } from '@/features/auth/useSession'
import { addCardItem } from '@/features/boards/AddCardForm'
import { BoardHeader, renameBoardItem } from '@/features/boards/BoardHeader'
import { BoardNotAvailable } from '@/features/boards/BoardNotAvailable'
import { CardDetailDialog } from '@/features/boards/CardDetailDialog'
import { ColumnRow } from '@/features/boards/ColumnRow'
import { DeleteBoardDialog } from '@/features/boards/DeleteBoardDialog'
import { KeptTextNotice, type KeptTextField } from '@/features/boards/KeptTextNotice'
import { describeBoardRefusal, type GoneItem } from '@/features/boards/boardRefusals'
import { keep, takeFor, type KeptDraft } from '@/features/boards/draftStore'
import { useBoardChange } from '@/features/boards/useBoardChange'

/**
 * SCR-04 — one board, at `/boards/:boardId`. It is only ever rendered for a recognised account: a
 * visitor at this address is shown the sign-in form and no board request is made (AC-27). Keyed by
 * the board, so a kept-text notice for one board never follows the member to another.
 */
export function BoardScreen() {
  const { boardId = '' } = useParams()
  const { state } = useSession()

  if (state.status !== 'account') {
    return null
  }

  return <BoardView key={boardId} boardId={boardId} accountId={state.account.id} />
}

const sessionNotRecognised = 'accounts.session_not_recognised'
const notAvailable = 'boards.not_available'
const ownerOnly = 'boards.owner_only'

/** A change whose target turned out to be gone (AC-18b), with what was typed into it. */
interface GoneText {
  heading: string
  kept: KeptDraft
}

/** A card that went while its dialog was open (AC-18b), with anything typed into it. */
interface GoneCard {
  heading: string
  fields: KeptTextField[]
}

interface BoardViewProps {
  boardId: string
  accountId: string
}

function BoardView({ boardId, accountId }: BoardViewProps) {
  const queryClient = useQueryClient()

  const board = useQuery({
    queryKey: boardQueryKey(boardId),
    queryFn: () => openBoard(boardId),
    // A failed read is shown at once with «Try again», and a 404 is an answer, not a failure.
    retry: false,
  })

  const [offer, setOffer] = useState(() => peekKeptDraft(accountId, boardId))
  const [gone, setGone] = useState<GoneText | undefined>(undefined)
  const [goneCard, setGoneCard] = useState<GoneCard | undefined>(undefined)
  const [openCardId, setOpenCardId] = useState<string | undefined>(undefined)
  const [deletingBoard, setDeletingBoard] = useState(false)

  /**
   * Every change on this screen reports its refusals here with what was typed, because the control
   * that held the text may be gone by the time the refusal is understood (a deleted column takes its
   * add-card form with it).
   */
  const onRefused = (error: unknown, kept: KeptDraft) => {
    if (!(error instanceof ApiError)) {
      return
    }

    if (error.code === sessionNotRecognised) {
      // AC-28: the shell turns to sign-in; the text waits for this account on this board.
      keep(accountId, kept)
    } else if (error.code === notAvailable) {
      const item = goneItemOf(kept.item)
      if (item !== undefined) {
        setGone({ heading: describeBoardRefusal(error, 'item-gone', item), kept })
      }
    } else if (error.code === ownerOnly) {
      // AC-22: the view believed this account owned the board; read it again so it stops offering.
      void queryClient.invalidateQueries({ queryKey: boardQueryKey(boardId) })
    }
  }

  const resubmit = useBoardChange<KeptDraft, BoardChange>({
    boardId,
    mutationFn: async (kept) => {
      try {
        return await submitKept(boardId, kept, () => {
          void queryClient.invalidateQueries({ queryKey: boardsQueryKey })
        })
      } catch (error) {
        onRefused(error, kept)
        throw error
      }
    },
    applyAccepted: (cached, change) => patchBoard(cached, change),
  })

  if (isNotAvailable(board.error)) {
    return <BoardNotAvailable />
  }

  if (board.data === undefined) {
    if (board.isError && !isSessionEnded(board.error)) {
      return (
        <main className="flex flex-col items-start gap-4">
          <div role="alert" className="flex flex-col gap-1">
            <h1 className="text-lg font-semibold">We could not load this board</h1>
            <p className="text-muted-foreground text-sm">Please check your connection and try again.</p>
          </div>
          <Button onClick={() => void board.refetch()}>Try again</Button>
        </main>
      )
    }

    return (
      <div role="status" aria-live="polite" className="text-muted-foreground text-sm">
        Loading the board…
      </div>
    )
  }

  const applyOffer = () => {
    const kept = takeFor(accountId)
    setOffer(undefined)
    if (kept !== undefined) {
      resubmit.change(kept)
    }
  }

  const discardOffer = () => {
    takeFor(accountId)
    setOffer(undefined)
  }

  const shownGone = gone !== undefined && targetIsGone(board.data, gone.kept) ? gone : undefined
  const resubmitRefusal =
    resubmit.error !== undefined && resubmit.resolvedAs === undefined
      ? describeBoardRefusal(resubmit.error, resubmit.resolvedAs)
      : undefined

  return (
    <main className="flex min-w-0 flex-col gap-4">
      <BoardHeader
        board={board.data}
        onRefused={onRefused}
        onDeleteBoard={() => setDeletingBoard(true)}
      />

      <div className="flex flex-col gap-2 empty:hidden">
        {offer !== undefined && (
          <KeptTextNotice
            heading="You were signed out before this was saved:"
            fields={fieldsOf(offer)}
            onApplyAgain={applyOffer}
            onDiscard={discardOffer}
          />
        )}
        {shownGone !== undefined && (
          <KeptTextNotice
            heading={shownGone.heading}
            fields={fieldsOf(shownGone.kept)}
            onDismiss={() => setGone(undefined)}
          />
        )}
        {goneCard !== undefined && (
          <KeptTextNotice
            heading={goneCard.heading}
            fields={goneCard.fields}
            onDismiss={() => setGoneCard(undefined)}
          />
        )}
        {resubmitRefusal !== undefined && (
          <p role="alert" className="text-destructive text-sm">
            {resubmitRefusal}
          </p>
        )}
      </div>

      <ColumnRow board={board.data} onRefused={onRefused} onOpenCard={setOpenCardId} />

      {openCardId !== undefined && (
        <CardDetailDialog
          key={openCardId}
          boardId={boardId}
          cardId={openCardId}
          summaryTitle={board.data.cards.find((card) => card.id === openCardId)?.title ?? ''}
          open
          onOpenChange={(open) => {
            if (!open) {
              setOpenCardId(undefined)
            }
          }}
          onCardGone={(heading, fields = []) => setGoneCard({ heading, fields })}
        />
      )}

      {/* Offered only to the owner: a re-read that says otherwise closes it (AC-22). */}
      {deletingBoard && board.data.is_owner && (
        <DeleteBoardDialog board={board.data} open onOpenChange={setDeletingBoard} />
      )}
    </main>
  )
}

// ---- kept text ------------------------------------------------------------------------------------

/** The kept items this screen can offer back, and which of them names a column or card that can go. */
const offeredItems: ReadonlyMap<string, GoneItem | undefined> = new Map([
  [addCardItem, 'column'],
  [renameBoardItem, undefined],
])

function goneItemOf(item: string): GoneItem | undefined {
  return offeredItems.get(item)
}

/**
 * Text this account kept for this board across a sign-in (AC-28), read without being spent: it
 * stays in storage until «Apply again» or «Discard» takes it, and a draft kept for anything else is
 * put back for the screen it belongs to.
 */
function peekKeptDraft(accountId: string, boardId: string): KeptDraft | undefined {
  const draft = takeFor(accountId)
  if (draft === undefined) {
    return undefined
  }

  keep(accountId, draft)
  return draft.boardId === boardId && offeredItems.has(draft.item) ? draft : undefined
}

/** Kept text resubmitted as an ordinary change, so it can be refused like any other. */
async function submitKept(
  boardId: string,
  kept: KeptDraft,
  onBoardRenamed: () => void,
): Promise<BoardChange> {
  const { fields } = kept

  if (kept.item === addCardItem) {
    const card = await addCard(boardId, {
      column_id: fields.column_id ?? '',
      title: fields.title ?? '',
      description: fields.description ? fields.description : undefined,
    })
    return { kind: 'card-added', card }
  }

  const renamed = await renameBoard(boardId, { name: fields.name ?? '' })
  onBoardRenamed()
  return { kind: 'board-renamed', board: renamed }
}

function fieldsOf(kept: KeptDraft): KeptTextField[] {
  const { fields } = kept

  if (kept.item === addCardItem) {
    const shown: KeptTextField[] = [{ label: 'Card title', value: fields.title ?? '' }]
    if (fields.description) {
      shown.push({ label: 'Description', value: fields.description })
    }
    return shown
  }

  return [{ label: 'Board name', value: fields.name ?? '' }]
}

/** Whether the column or card a kept change named is no longer on the board as last read. */
function targetIsGone(board: Board, kept: KeptDraft): boolean {
  if (kept.item === addCardItem) {
    return !board.columns.some((column) => column.id === kept.fields.column_id)
  }
  return false
}

// ---- refusals of the read ------------------------------------------------------------------------

function isNotAvailable(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404
}

function isSessionEnded(error: unknown): boolean {
  return error instanceof ApiError && error.isNotRecognised
}
