import { useQuery } from '@tanstack/react-query'
import { useState } from 'react'
import { useNavigate } from 'react-router'

import { ApiError } from '@/api/accounts'
import { boardsQueryKey, listMyBoards, type BoardListEntry } from '@/api/boards'
import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { useSession } from '@/features/auth/useSession'
import { CreateBoardDialog, keptBoardNameItem } from '@/features/boards/CreateBoardDialog'
import { KeptTextNotice } from '@/features/boards/KeptTextNotice'
import { PlainText } from '@/features/boards/PlainText'
import { keep, takeFor } from '@/features/boards/draftStore'

/**
 * SCR-02 — the board list, at `/` (AC-04). Every board the account is a member of, newest first
 * exactly as the server ordered them, with the owned ones marked. A 401 on the read ends the session
 * through the query client's shared rule, so this screen has no session-ended state of its own.
 */
export function BoardListScreen() {
  const { state } = useSession()
  const accountId = state.status === 'account' ? state.account.id : undefined

  const boards = useQuery({
    queryKey: boardsQueryKey,
    queryFn: () => listMyBoards(),
    // A failed read is shown at once with «Try again»; the member decides when to ask again.
    retry: false,
  })

  const [keptName, setKeptName] = useState(() => peekKeptBoardName(accountId))
  const [dialog, setDialog] = useState<{ initialName?: string } | null>(null)

  if (accountId === undefined) {
    return null
  }

  const openDialog = (initialName?: string) => setDialog({ initialName })

  const applyKeptName = () => {
    takeFor(accountId)
    setKeptName(undefined)
    openDialog(keptName)
  }

  const discardKeptName = () => {
    takeFor(accountId)
    setKeptName(undefined)
  }

  return (
    <main className="flex flex-col gap-6">
      {keptName !== undefined && (
        <KeptTextNotice
          heading="You were signed out before this was saved:"
          fields={[{ label: 'Board name', value: keptName }]}
          onApplyAgain={applyKeptName}
          onDiscard={discardKeptName}
        />
      )}

      <BoardListBody
        boards={boards.data?.items}
        isPending={boards.isPending}
        failed={boards.isError && !isSessionEnded(boards.error)}
        onRetry={() => void boards.refetch()}
        onCreate={() => openDialog()}
      />

      {dialog !== null && (
        <CreateBoardDialog
          open
          onOpenChange={(open) => {
            if (!open) setDialog(null)
          }}
          accountId={accountId}
          initialName={dialog.initialName}
        />
      )}
    </main>
  )
}

interface BoardListBodyProps {
  boards: BoardListEntry[] | undefined
  isPending: boolean
  failed: boolean
  onRetry: () => void
  onCreate: () => void
}

function BoardListBody({ boards, isPending, failed, onRetry, onCreate }: BoardListBodyProps) {
  const navigate = useNavigate()

  if (failed) {
    return (
      <div className="flex flex-col items-start gap-4">
        <div role="alert" className="flex flex-col gap-1">
          <h1 className="text-lg font-semibold">We could not load your boards</h1>
          <p className="text-muted-foreground text-sm">Please check your connection and try again.</p>
        </div>
        <Button onClick={onRetry}>Try again</Button>
      </div>
    )
  }

  if (isPending || boards === undefined) {
    return (
      <div role="status" aria-live="polite" className="text-muted-foreground text-sm">
        Loading your boards…
      </div>
    )
  }

  if (boards.length === 0) {
    return (
      <section className="flex flex-col gap-4">
        <h1 className="text-lg font-semibold">My boards</h1>
        <Card>
          <CardContent className="flex flex-col items-start gap-4 pt-6">
            <p className="text-sm">You have no boards yet.</p>
            <Button onClick={onCreate}>Create board</Button>
          </CardContent>
        </Card>
      </section>
    )
  }

  return (
    <section className="flex flex-col gap-4">
      <div className="flex items-center justify-between gap-4">
        <h1 className="text-lg font-semibold">My boards</h1>
        <Button onClick={onCreate}>Create board</Button>
      </div>
      <ul className="flex flex-col gap-2">
        {boards.map((board) => (
          <li key={board.id}>
            <Button
              variant="outline"
              className="h-auto w-full justify-between gap-4 py-3 text-left"
              onClick={() => void navigate(`/boards/${encodeURIComponent(board.id)}`)}
            >
              <PlainText text={board.name} className="min-w-0" />
              {board.is_owner && (
                <span className="text-muted-foreground shrink-0 text-xs">Owner</span>
              )}
            </Button>
          </li>
        ))}
      </ul>
    </section>
  )
}

function isSessionEnded(error: unknown): boolean {
  return error instanceof ApiError && error.isNotRecognised
}

/**
 * A board name this account kept across a sign-in (AC-28), read without being spent: it stays in
 * storage until «Apply again» or «Discard» takes it, and a draft kept for anything else is put back
 * for the screen it belongs to.
 */
function peekKeptBoardName(accountId: string | undefined): string | undefined {
  if (accountId === undefined) {
    return undefined
  }

  const draft = takeFor(accountId)
  if (draft === undefined) {
    return undefined
  }

  keep(accountId, draft)
  return draft.item === keptBoardNameItem ? draft.fields.name : undefined
}
