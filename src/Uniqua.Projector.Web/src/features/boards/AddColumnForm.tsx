import { useState } from 'react'

import { addColumn, patchBoard, type AddColumnRequest, type AddedColumn } from '@/api/boards'
import { Button } from '@/components/ui/button'
import { InlineNameEditor } from '@/features/boards/InlineNameEditor'
import { describeBoardRefusal } from '@/features/boards/boardRefusals'
import type { KeptDraft } from '@/features/boards/draftStore'
import { useBoardChange } from '@/features/boards/useBoardChange'

/** The item a kept add-column is filed under in `draftStore`. */
export const addColumnItem = 'add_column'

export interface AddColumnFormProps {
  boardId: string
  /** Every refusal, with what was typed, for the screen to act on (kept text, re-reads). */
  onRefused: (error: unknown, kept: KeptDraft) => void
}

/**
 * The add-column slot at the end of the row (AC-05). Closed it is «+ Add column»; open it is the
 * shared `InlineNameEditor`. The control is always offered, even on a full board: the server is the
 * judge of the 20-column ceiling (AC-11), and its refusal is shown here with the typed name kept
 * (AC-08). An accepted column lands last through the cache patch, and the editor closes.
 */
export function AddColumnForm({ boardId, onRefused }: AddColumnFormProps) {
  const [open, setOpen] = useState(false)
  const [refusalShown, setRefusalShown] = useState(false)

  const add = useBoardChange<AddColumnRequest, AddedColumn>({
    boardId,
    mutationFn: async (body) => {
      try {
        const added = await addColumn(boardId, body)
        setOpen(false)
        return added
      } catch (error) {
        onRefused(error, { boardId, item: addColumnItem, fields: { name: body.name } })
        throw error
      }
    },
    applyAccepted: (board, added) => patchBoard(board, { kind: 'column-added', added }),
  })

  if (!open) {
    return (
      <Button
        variant="outline"
        className="w-72 shrink-0 justify-start"
        onClick={() => {
          setRefusalShown(false)
          setOpen(true)
        }}
      >
        + Add column
      </Button>
    )
  }

  const refusal =
    refusalShown && add.error !== undefined
      ? describeBoardRefusal(add.error, add.resolvedAs, 'column')
      : undefined

  return (
    <div className="bg-muted/40 w-72 shrink-0 rounded-lg border p-3">
      <InlineNameEditor
        id="add-column-name"
        label="Column name"
        hint="Between 1 and 50 characters."
        refusal={refusal}
        pending={add.isPending}
        saveLabel="Add"
        pendingLabel="Adding…"
        onSave={(name) => {
          setRefusalShown(true)
          add.change({ name })
        }}
        onCancel={() => setOpen(false)}
      />
    </div>
  )
}
