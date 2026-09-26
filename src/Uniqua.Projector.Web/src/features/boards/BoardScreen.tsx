import { useQuery, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { useState } from 'react'
import { useParams } from 'react-router'

import { ApiError } from '@/api/accounts'
import {
  addCard,
  addColumn,
  boardQueryKey,
  boardsQueryKey,
  cardQueryKey,
  editCard,
  openBoard,
  patchBoard,
  renameBoard,
  renameColumn,
  type Board,
  type BoardChange,
} from '@/api/boards'
import { Button } from '@/components/ui/button'
import { useSession } from '@/features/auth/useSession'
import { addCardItem } from '@/features/boards/AddCardForm'
import { addColumnItem } from '@/features/boards/AddColumnForm'
import { BoardHeader, renameBoardItem } from '@/features/boards/BoardHeader'
import { BoardNotAvailable } from '@/features/boards/BoardNotAvailable'
import { CardDetailDialog, editCardItem } from '@/features/boards/CardDetailDialog'
import { renameColumnItem } from '@/features/boards/ColumnHeader'
import { ColumnRow } from '@/features/boards/ColumnRow'
import { DeleteBoardDialog, deleteBoardItem } from '@/features/boards/DeleteBoardDialog'
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
  // A card that went while its dialog was only being read or deleted: nothing typed to keep.
  const [goneCard, setGoneCard] = useState<string | undefined>(undefined)
  const [openCardId, setOpenCardId] = useState<string | undefined>(undefined)
  // The board deletion dialog, open with the name to start from (kept across a sign-in, or empty).
  const [deletingBoard, setDeletingBoard] = useState<string | undefined>(undefined)

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
        return await submitKept(queryClient, boardId, kept)
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
    if (kept === undefined) {
      return
    }
    if (kept.item === deleteBoardItem) {
      // A deletion is never applied by «Apply again»: it only reopens its dialog, filled in.
      setDeletingBoard(kept.fields.confirm_name ?? '')
    } else {
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
        onDeleteBoard={() => setDeletingBoard('')}
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
          <KeptTextNotice heading={goneCard} fields={[]} onDismiss={() => setGoneCard(undefined)} />
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
          onCardGone={setGoneCard}
          onRefused={onRefused}
        />
      )}

      {/* Offered only to the owner: a re-read that says otherwise closes it (AC-22). */}
      {deletingBoard !== undefined && board.data.is_owner && (
        <DeleteBoardDialog
          board={board.data}
          open
          initialConfirmName={deletingBoard}
          onOpenChange={(open) => {
            if (!open) {
              setDeletingBoard(undefined)
            }
          }}
          onRefused={onRefused}
        />
      )}
    </main>
  )
}

// ---- kept text ------------------------------------------------------------------------------------

/** Every change that carries typed text, so every one this screen can offer back (AC-28). */
const offeredItems: ReadonlySet<string> = new Set([
  addCardItem,
  renameBoardItem,
  addColumnItem,
  renameColumnItem,
  editCardItem,
  deleteBoardItem,
])

/** The kept changes that name a column or card that can go meanwhile, and which of the two (AC-18b). */
const goneItems: ReadonlyMap<string, GoneItem> = new Map([
  [addCardItem, 'column'],
  [renameColumnItem, 'column'],
  [editCardItem, 'card'],
])

function goneItemOf(item: string): GoneItem | undefined {
  return goneItems.get(item)
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

/**
 * Kept text resubmitted as an ordinary change, so it can be refused like any other. A column rename
 * and a card edit go against the version the board has now — read afresh, since nothing reached
 * this tab while it was signed out — because the member has just chosen to apply this text. The
 * version typed against is sent only for a column or card no longer on the board, which the server
 * then refuses as gone (AC-18b).
 */
async function submitKept(
  queryClient: QueryClient,
  boardId: string,
  kept: KeptDraft,
): Promise<BoardChange> {
  const { fields } = kept

  switch (kept.item) {
    case addCardItem: {
      const card = await addCard(boardId, {
        column_id: fields.column_id ?? '',
        title: fields.title ?? '',
        description: fields.description ? fields.description : undefined,
      })
      return { kind: 'card-added', card }
    }

    case addColumnItem: {
      const added = await addColumn(boardId, { name: fields.name ?? '' })
      return { kind: 'column-added', added }
    }

    case renameColumnItem: {
      const columnId = fields.column_id ?? ''
      const board = await readBoardNow(queryClient, boardId)
      const current = board.columns.find((column) => column.id === columnId)
      const column = await renameColumn(boardId, columnId, {
        name: fields.name ?? '',
        name_version: current?.name_version ?? Number(fields.name_version),
      })
      return { kind: 'column-renamed', column }
    }

    case editCardItem: {
      const cardId = fields.card_id ?? ''
      const board = await readBoardNow(queryClient, boardId)
      const current = board.cards.find((card) => card.id === cardId)
      const card = await editCard(boardId, cardId, {
        title: fields.title ?? '',
        description: fields.description ?? '',
        content_version: current?.content_version ?? Number(fields.content_version),
      })
      queryClient.setQueryData(cardQueryKey(boardId, card.id), card)
      return { kind: 'card-edited', card }
    }

    default: {
      const renamed = await renameBoard(boardId, { name: fields.name ?? '' })
      void queryClient.invalidateQueries({ queryKey: boardsQueryKey })
      return { kind: 'board-renamed', board: renamed }
    }
  }
}

/** The board as the server has it now, written to the cache as any read of it is. */
function readBoardNow(queryClient: QueryClient, boardId: string): Promise<Board> {
  return queryClient.fetchQuery({
    queryKey: boardQueryKey(boardId),
    queryFn: () => openBoard(boardId),
    staleTime: 0,
    retry: false,
  })
}

function fieldsOf(kept: KeptDraft): KeptTextField[] {
  const { fields } = kept

  switch (kept.item) {
    case addCardItem:
    case editCardItem: {
      const shown: KeptTextField[] = [{ label: 'Card title', value: fields.title ?? '' }]
      if (fields.description) {
        shown.push({ label: 'Description', value: fields.description })
      }
      return shown
    }

    case addColumnItem:
    case renameColumnItem:
      return [{ label: 'Column name', value: fields.name ?? '' }]

    case deleteBoardItem:
      return [{ label: 'Name typed to delete the board', value: fields.confirm_name ?? '' }]

    default:
      return [{ label: 'Board name', value: fields.name ?? '' }]
  }
}

/** Whether the column or card a kept change named is no longer on the board as last read. */
function targetIsGone(board: Board, kept: KeptDraft): boolean {
  switch (goneItemOf(kept.item)) {
    case 'column':
      return !board.columns.some((column) => column.id === kept.fields.column_id)
    case 'card':
      return !board.cards.some((card) => card.id === kept.fields.card_id)
    default:
      return false
  }
}

// ---- refusals of the read ------------------------------------------------------------------------

function isNotAvailable(error: unknown): boolean {
  return error instanceof ApiError && error.status === 404
}

function isSessionEnded(error: unknown): boolean {
  return error instanceof ApiError && error.isNotRecognised
}
