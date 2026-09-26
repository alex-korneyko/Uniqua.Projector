import type { DraggableAttributes, DraggableSyntheticListeners } from '@dnd-kit/core'
import { GripVertical, Pencil, Trash2 } from 'lucide-react'
import { useState } from 'react'

import { ApiError } from '@/api/accounts'
import {
  deleteColumn,
  patchBoard,
  renameColumn,
  type Column,
  type ColumnLayout,
  type DeleteColumnRequest,
  type RenameColumnRequest,
} from '@/api/boards'
import { Button } from '@/components/ui/button'
import { buttonVariants } from '@/components/ui/button-variants'
import { InlineNameEditor } from '@/features/boards/InlineNameEditor'
import { PlainText } from '@/features/boards/PlainText'
import { describeBoardRefusal } from '@/features/boards/boardRefusals'
import type { KeptDraft } from '@/features/boards/draftStore'
import { useBoardChange } from '@/features/boards/useBoardChange'
import { cn } from '@/lib/utils'

/** The items a kept column rename / delete is filed under in `draftStore`. */
export const renameColumnItem = 'rename_column'
export const deleteColumnItem = 'delete_column'

const columnRenamed = 'boards.column_renamed'

/** What `BoardColumn`'s sortable hands the header, so the handle alone starts a drag. */
export interface ColumnDragHandle {
  setActivatorNodeRef: (element: HTMLElement | null) => void
  attributes: DraggableAttributes
  listeners: DraggableSyntheticListeners
}

export interface ColumnHeaderProps {
  boardId: string
  column: Column
  handle: ColumnDragHandle
  /** Every refusal, with what was typed, for the screen to act on (kept text, re-reads). */
  onRefused: (error: unknown, kept: KeptDraft) => void
}

type LastChange = 'rename' | 'delete' | undefined

/**
 * A column's header: the drag handle, the name as `PlainText`, «Rename column» and «Delete column»,
 * with each refusal shown right here, under the header or in the editor. Both changes send the
 * `name_version` last seen; a stale refusal patches the column to its current name, so a second
 * «Save» applies the typed name against the new version (AC-06b). Delete asks for no confirmation
 * and the trash is never disabled beforehand: only an empty column can go, and the server says so
 * (AC-09, AC-10).
 */
export function ColumnHeader({ boardId, column, handle, onRefused }: ColumnHeaderProps) {
  const { setActivatorNodeRef, attributes, listeners } = handle
  const [editing, setEditing] = useState(false)
  const [lastChange, setLastChange] = useState<LastChange>(undefined)

  const rename = useBoardChange<RenameColumnRequest, Column>({
    boardId,
    mutationFn: async (body) => {
      try {
        const renamed = await renameColumn(boardId, column.id, body)
        setEditing(false)
        return renamed
      } catch (error) {
        // The version typed against goes with it, for a column no longer on the board when the
        // name is applied again (AC-28): the server, not this client, then says it is gone.
        onRefused(error, {
          boardId,
          item: renameColumnItem,
          fields: { column_id: column.id, name: body.name, name_version: String(body.name_version) },
        })
        throw error
      }
    },
    applyAccepted: (board, renamed) => patchBoard(board, { kind: 'column-renamed', column: renamed }),
  })

  const remove = useBoardChange<DeleteColumnRequest, ColumnLayout>({
    boardId,
    mutationFn: async (body) => {
      try {
        return await deleteColumn(boardId, column.id, body)
      } catch (error) {
        onRefused(error, { boardId, item: deleteColumnItem, fields: { column_id: column.id } })
        throw error
      }
    },
    applyAccepted: (board, layout) => patchBoard(board, { kind: 'layout-changed', layout }),
  })

  const startEditing = () => {
    setLastChange(undefined)
    setEditing(true)
  }

  const stopEditing = () => {
    setLastChange(undefined)
    setEditing(false)
  }

  const save = (name: string) => {
    setLastChange('rename')
    rename.change({ name, name_version: column.name_version })
  }

  const startDelete = () => {
    setLastChange('delete')
    remove.change({ name_version: column.name_version })
  }

  const renameRefusal =
    lastChange === 'rename' && rename.error !== undefined
      ? describeBoardRefusal(rename.error, rename.resolvedAs, 'column')
      : undefined
  // A gone column is refreshed away by the re-read; nothing further is shown for it here.
  const deleteRefusal =
    lastChange === 'delete' && remove.error !== undefined && remove.resolvedAs !== 'item-gone'
      ? describeBoardRefusal(remove.error, remove.resolvedAs, 'column')
      : undefined

  if (editing) {
    return (
      <div className="flex min-w-0 flex-col gap-2">
        <InlineNameEditor
          id={`column-name-${column.id}`}
          label="Column name"
          initialValue={column.name}
          hint="Between 1 and 50 characters."
          refusal={renameRefusal}
          pending={rename.isPending}
          onSave={save}
          onCancel={stopEditing}
        />
        {renameRefusal !== undefined && isCode(rename.error, columnRenamed) && (
          <p className="text-muted-foreground text-sm">
            Its current name is <PlainText text={column.name} className="font-semibold" />
          </p>
        )}
      </div>
    )
  }

  return (
    <div className="flex min-w-0 flex-col gap-2">
      <div className="flex min-w-0 items-center gap-1">
        <button
          ref={setActivatorNodeRef}
          type="button"
          className={cn(buttonVariants({ variant: 'ghost', size: 'sm' }), 'shrink-0 cursor-grab touch-none px-1')}
          {...attributes}
          {...listeners}
          aria-label={`Move column ${column.name}`}
        >
          <GripVertical aria-hidden="true" />
        </button>
        <h2 className="min-w-0 flex-1 text-sm font-semibold">
          <PlainText text={column.name} />
        </h2>
        <Button
          variant="ghost"
          size="sm"
          className="shrink-0 px-2"
          aria-label="Rename column"
          onClick={startEditing}
        >
          <Pencil aria-hidden="true" />
        </Button>
        <Button
          variant="ghost"
          size="sm"
          className="shrink-0 px-2"
          aria-label="Delete column"
          disabled={remove.isPending}
          onClick={startDelete}
        >
          <Trash2 aria-hidden="true" />
        </Button>
      </div>

      {deleteRefusal !== undefined && (
        <p role="alert" className="text-destructive text-sm">
          {deleteRefusal}
        </p>
      )}
    </div>
  )
}

function isCode(error: unknown, code: string): boolean {
  return error instanceof ApiError && error.code === code
}
