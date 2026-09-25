import { useQueryClient } from '@tanstack/react-query'
import { Pencil } from 'lucide-react'
import { useState } from 'react'
import { useNavigate } from 'react-router'

import {
  boardsQueryKey,
  patchBoard,
  renameBoard,
  type Board,
  type BoardListPage,
  type BoardName,
  type RenameBoardRequest,
} from '@/api/boards'
import { Button } from '@/components/ui/button'
import { InlineNameEditor } from '@/features/boards/InlineNameEditor'
import { PlainText } from '@/features/boards/PlainText'
import { describeBoardRefusal } from '@/features/boards/boardRefusals'
import type { KeptDraft } from '@/features/boards/draftStore'
import { useBoardChange } from '@/features/boards/useBoardChange'

/** The item a kept board rename is filed under in `draftStore`, offered back by SCR-04. */
export const renameBoardItem = 'rename_board'

export interface BoardHeaderProps {
  board: Board
  /** Every refusal of a change made here, with what was typed, for the screen to act on. */
  onRefused: (error: unknown, kept: KeptDraft) => void
  /** «Delete board» → SCR-07, wired by T22. */
  onDeleteBoard?: () => void
}

/**
 * SCR-04's header: «← My boards», the board name as `PlainText` in the page's one `h1`, and — for
 * the owner only (AC-21, AC-22) — «Rename board» and «Delete board». The owner controls follow
 * `is_owner` as last read, so an owner-only refusal that re-reads the board removes them.
 */
export function BoardHeader({ board, onRefused, onDeleteBoard }: BoardHeaderProps) {
  const navigate = useNavigate()
  const queryClient = useQueryClient()
  const [editing, setEditing] = useState(false)
  const [refusalShown, setRefusalShown] = useState(false)

  const rename = useBoardChange<RenameBoardRequest, BoardName>({
    boardId: board.id,
    mutationFn: async (body) => {
      try {
        const renamed = await renameBoard(board.id, body)
        // AC-19: every member's list shows the new name, this one's included, without a re-read.
        queryClient.setQueryData<BoardListPage>(boardsQueryKey, (page) =>
          page === undefined ? undefined : renamedInList(page, renamed),
        )
        setEditing(false)
        return renamed
      } catch (error) {
        onRefused(error, { boardId: board.id, item: renameBoardItem, fields: { name: body.name } })
        throw error
      }
    },
    applyAccepted: (cached, renamed) => patchBoard(cached, { kind: 'board-renamed', board: renamed }),
  })

  const refusal =
    refusalShown && rename.error !== undefined
      ? describeBoardRefusal(rename.error, rename.resolvedAs)
      : undefined

  const startEditing = () => {
    setRefusalShown(false)
    setEditing(true)
  }

  const stopEditing = () => {
    setRefusalShown(false)
    setEditing(false)
  }

  const save = (name: string) => {
    setRefusalShown(true)
    rename.change({ name })
  }

  const isEditing = board.is_owner && editing

  return (
    <header className="flex flex-col gap-2">
      <div className="flex min-w-0 items-center gap-3">
        <Button variant="ghost" size="sm" className="shrink-0" onClick={() => void navigate('/')}>
          <span aria-hidden="true">←</span> My boards
        </Button>

        {isEditing ? (
          <div className="min-w-0 flex-1">
            <InlineNameEditor
              id="board-name"
              label="Board name"
              initialValue={board.name}
              hint="Between 1 and 100 characters."
              refusal={refusal}
              pending={rename.isPending}
              onSave={save}
              onCancel={stopEditing}
            />
          </div>
        ) : (
          <>
            <h1 className="min-w-0 text-lg font-semibold">
              <PlainText text={board.name} />
            </h1>
            {board.is_owner && (
              <Button
                variant="ghost"
                size="sm"
                className="shrink-0"
                aria-label="Rename board"
                onClick={startEditing}
              >
                <Pencil aria-hidden="true" />
              </Button>
            )}
          </>
        )}

        {board.is_owner && (
          <Button variant="outline" size="sm" className="ml-auto shrink-0" onClick={onDeleteBoard}>
            Delete board
          </Button>
        )}
      </div>

      {/* Outside the editor, so an owner-only refusal outlives the controls it removes (AC-22). */}
      {!isEditing && refusal !== undefined && (
        <p role="alert" className="text-destructive text-sm">
          {refusal}
        </p>
      )}
    </header>
  )
}

function renamedInList(page: BoardListPage, renamed: BoardName): BoardListPage {
  return {
    ...page,
    items: page.items.map((entry) =>
      entry.id === renamed.id ? { ...entry, name: renamed.name } : entry,
    ),
  }
}
