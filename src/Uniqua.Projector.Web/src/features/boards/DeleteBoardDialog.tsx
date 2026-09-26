import { useQueryClient } from '@tanstack/react-query'
import { useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'

import { ApiError } from '@/api/accounts'
import {
  boardQueryKey,
  boardsQueryKey,
  currentStateOf,
  deleteBoard,
  openBoard,
  type Board,
  type BoardListPage,
  type DeleteBoardRequest,
} from '@/api/boards'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { PlainText } from '@/features/boards/PlainText'
import { describeBoardRefusal } from '@/features/boards/boardRefusals'
import { useBoardChange } from '@/features/boards/useBoardChange'

export interface DeleteBoardDialogProps {
  board: Board
  open: boolean
  onOpenChange: (open: boolean) => void
}

const ownerOnly = 'boards.owner_only'

/**
 * SCR-07 «Confirm board deletion» (AC-20, AC-20b), offered only to the owner. The typed name is sent
 * as typed and «Delete board» is always enabled, because the server trims and compares it. A
 * mismatch shows `current_name` in place of the name as last read (the board cache is patched by
 * `useBoardChange`) and keeps the typed text; an accepted deletion leaves for SCR-02 with the board
 * gone from the list cache and its own query dropped.
 */
export function DeleteBoardDialog({ board, open, onOpenChange }: DeleteBoardDialogProps) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()
  const [confirmName, setConfirmName] = useState('')

  const remove = useBoardChange<DeleteBoardRequest, void>({
    boardId: board.id,
    mutationFn: async (body) => {
      try {
        await deleteBoard(board.id, body)
      } catch (error) {
        if (error instanceof ApiError && error.status === 404) {
          // Already gone: SCR-08 takes over from the board query once it is read again.
          onOpenChange(false)
        } else if (error instanceof ApiError && error.code === ownerOnly) {
          void closeUnlessOwner()
        }
        throw error
      }

      queryClient.setQueryData<BoardListPage>(boardsQueryKey, (page) =>
        page === undefined
          ? undefined
          : { ...page, items: page.items.filter((entry) => entry.id !== board.id) },
      )
      await navigate('/')
      queryClient.removeQueries({ queryKey: boardQueryKey(board.id) })
    },
  })

  /** AC-22: read the board again, and close once it says this account no longer owns it. */
  const closeUnlessOwner = async () => {
    try {
      const fresh = await queryClient.fetchQuery({
        queryKey: boardQueryKey(board.id),
        queryFn: () => openBoard(board.id),
        staleTime: 0,
        retry: false,
      })
      if (!fresh.is_owner) {
        onOpenChange(false)
      }
    } catch {
      // No answer about the board: the refusal line stays, and nothing else is claimed.
    }
  }

  const currentName =
    remove.error instanceof ApiError ? currentStateOf(remove.error).current_name : undefined
  const shownName = currentName ?? board.name
  const refusal =
    remove.error !== undefined ? describeBoardRefusal(remove.error, remove.resolvedAs) : undefined

  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    remove.change({ confirm_name: confirmName })
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent aria-describedby={undefined}>
        <DialogHeader>
          <DialogTitle>Delete board</DialogTitle>
        </DialogHeader>

        <form noValidate className="flex flex-col gap-4" onSubmit={onSubmit}>
          <p className="text-sm">
            This deletes the board with all its columns and cards. It cannot be undone.
          </p>
          <PlainText as="p" text={shownName} className="font-bold" />

          <div className="flex flex-col gap-2">
            <Label htmlFor="delete-board-name">Type the board's name to confirm</Label>
            <Input
              id="delete-board-name"
              autoComplete="off"
              aria-invalid={refusal !== undefined ? true : undefined}
              value={confirmName}
              onChange={(event) => setConfirmName(event.target.value)}
            />
          </div>

          {refusal !== undefined && (
            <p role="alert" className="text-destructive text-sm">
              {refusal}
            </p>
          )}

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" variant="destructive" disabled={remove.isPending}>
              {remove.isPending ? 'Deleting…' : 'Delete board'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
