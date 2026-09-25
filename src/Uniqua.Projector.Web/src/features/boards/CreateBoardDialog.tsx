import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useRef, useState, type FormEvent } from 'react'
import { useNavigate } from 'react-router'

import { ApiError } from '@/api/accounts'
import { boardQueryKey, boardsQueryKey, createBoard, type Board } from '@/api/boards'
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
import { describeBoardRefusal } from '@/features/boards/boardRefusals'
import { keep } from '@/features/boards/draftStore'

/**
 * SCR-03 — the create-board dialog (AC-01, AC-02, AC-03). It calls `createBoard`, keeps what was
 * typed on every refusal, and on a 201 seeds the board query from the answer so SCR-04 opens with
 * its three columns and no second request.
 */
export interface CreateBoardDialogProps {
  open: boolean
  onOpenChange: (open: boolean) => void
  accountId: string
  /** A name kept across a sign-in (AC-28), offered back through «Apply again». */
  initialName?: string
}

/** The item a kept board name is filed under in `draftStore`, read back by SCR-02. */
export const keptBoardNameItem = 'board_name'

const nameLimit = 100

// The server's own wording for `boards.board_name_invalid`, so a refusal made here before sending
// reads exactly as the one the server would have made (edge case: a name of only spaces).
const nameInvalid = 'The board name is not usable. A board name must be between 1 and 100 characters.'

const limitReachedCode = 'boards.owned_board_limit_reached'
const sessionNotRecognised = 'accounts.session_not_recognised'

const surroundingWhitespace = /^\p{White_Space}+|\p{White_Space}+$/gu

/** The Text rule (sad.md §8): Unicode whitespace trimmed, length counted in code points. */
function isUsableName(name: string): boolean {
  const length = [...name.replace(surroundingWhitespace, '')].length
  return length >= 1 && length <= nameLimit
}

export function CreateBoardDialog({
  open,
  onOpenChange,
  accountId,
  initialName = '',
}: CreateBoardDialogProps) {
  const queryClient = useQueryClient()
  const navigate = useNavigate()

  const [name, setName] = useState(initialName)
  const [refusal, setRefusal] = useState<string | null>(null)
  // AC-03: once the limit answers, «Create» stays disabled for as long as this dialog is open.
  const [limitReached, setLimitReached] = useState(false)

  const inputRef = useRef<HTMLInputElement>(null)

  const refuse = (message: string) => {
    setRefusal(message)
    inputRef.current?.focus()
  }

  const create = useMutation({
    mutationFn: createBoard,
    onSuccess: (board: Board) => {
      queryClient.setQueryData(boardQueryKey(board.id), board)
      void queryClient.invalidateQueries({ queryKey: boardsQueryKey })
      onOpenChange(false)
      void navigate(`/boards/${encodeURIComponent(board.id)}`)
    },
    onError: (error: unknown) => {
      if (error instanceof ApiError && error.code === sessionNotRecognised) {
        // AC-28: the shell turns to sign-in; the name waits for this account on SCR-02.
        keep(accountId, { item: keptBoardNameItem, fields: { name } })
      }
      if (error instanceof ApiError && error.code === limitReachedCode) {
        setLimitReached(true)
      }
      refuse(describeBoardRefusal(error))
    },
  })

  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()

    if (!isUsableName(name)) {
      refuse(nameInvalid)
      return
    }

    setRefusal(null)
    create.mutate({ name })
  }

  return (
    <Dialog open={open} onOpenChange={onOpenChange}>
      <DialogContent
        aria-describedby={undefined}
        onOpenAutoFocus={(event) => {
          event.preventDefault()
          inputRef.current?.focus()
        }}
      >
        <DialogHeader>
          <DialogTitle>Create board</DialogTitle>
        </DialogHeader>

        <form noValidate className="flex flex-col gap-4" onSubmit={onSubmit}>
          <div className="flex flex-col gap-2">
            <Label htmlFor="create-board-name">Board name</Label>
            <Input
              ref={inputRef}
              id="create-board-name"
              autoComplete="off"
              aria-describedby="create-board-name-hint"
              aria-invalid={refusal !== null && !limitReached ? true : undefined}
              value={name}
              onChange={(event) => setName(event.target.value)}
            />
            <p id="create-board-name-hint" className="text-muted-foreground text-xs">
              Between 1 and 100 characters.
            </p>
            {refusal !== null && (
              <p role="alert" className="text-destructive text-sm">
                {refusal}
              </p>
            )}
          </div>

          <DialogFooter>
            <Button type="button" variant="ghost" onClick={() => onOpenChange(false)}>
              Cancel
            </Button>
            <Button type="submit" disabled={create.isPending || limitReached}>
              {create.isPending ? 'Creating…' : 'Create'}
            </Button>
          </DialogFooter>
        </form>
      </DialogContent>
    </Dialog>
  )
}
