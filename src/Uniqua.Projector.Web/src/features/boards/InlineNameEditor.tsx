import { useState, type FormEvent } from 'react'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'

/**
 * One name, edited in place: the board title (T20), and the column add and rename (T21). It owns
 * only the text being typed, so a refusal leaves that text exactly where it was (AC-18b, AC-28);
 * whether the save is pending and what the refusal says belong to the change that owns it.
 */
export interface InlineNameEditorProps {
  /** The input's id; the hint and the refusal line are bound to it. */
  id: string
  /** The field's accessible name — «Board name», «Column name». */
  label: string
  initialValue?: string
  /** The hint line under the input — «Between 1 and 100 characters.» */
  hint: string
  /** The refusal line under the editor, when the last save was refused. */
  refusal?: string
  pending?: boolean
  saveLabel?: string
  pendingLabel?: string
  onSave: (value: string) => void
  onCancel: () => void
}

export function InlineNameEditor({
  id,
  label,
  initialValue = '',
  hint,
  refusal,
  pending = false,
  saveLabel = 'Save',
  pendingLabel = 'Saving…',
  onSave,
  onCancel,
}: InlineNameEditorProps) {
  const [value, setValue] = useState(initialValue)
  const hintId = `${id}-hint`

  const onSubmit = (event: FormEvent<HTMLFormElement>) => {
    event.preventDefault()
    onSave(value)
  }

  return (
    <form noValidate className="flex min-w-0 flex-col gap-2" onSubmit={onSubmit}>
      <Label htmlFor={id} className="sr-only">
        {label}
      </Label>
      <div className="flex min-w-0 items-center gap-2">
        <Input
          id={id}
          autoFocus
          autoComplete="off"
          aria-describedby={hintId}
          aria-invalid={refusal !== undefined ? true : undefined}
          value={value}
          onChange={(event) => setValue(event.target.value)}
        />
        <Button type="submit" size="sm" disabled={pending}>
          {pending ? pendingLabel : saveLabel}
        </Button>
        <Button type="button" variant="ghost" size="sm" onClick={onCancel}>
          Cancel
        </Button>
      </div>
      <p id={hintId} className="text-muted-foreground text-xs">
        {hint}
      </p>
      {refusal !== undefined && (
        <p role="alert" className="text-destructive text-sm">
          {refusal}
        </p>
      )}
    </form>
  )
}
