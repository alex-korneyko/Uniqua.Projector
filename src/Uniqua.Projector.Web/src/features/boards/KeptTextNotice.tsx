import { Button } from '@/components/ui/button'
import { Card, CardContent } from '@/components/ui/card'
import { PlainText } from '@/features/boards/PlainText'

// Keeps typed text visible when the control that held it is gone (AC-18b) or across a sign-in
// (AC-28). The text is shown through PlainText so it can be selected and copied, never edited in
// place. With onApplyAgain it is the kept-text offer («Apply again» / «Discard»); without it, the
// kept-text gone notice («Dismiss»), because there is nothing left to apply it to.
export interface KeptTextField {
  label: string
  value: string
}

export interface KeptTextNoticeProps {
  heading: string
  fields: KeptTextField[]
  onApplyAgain?: () => void
  onDiscard?: () => void
  onDismiss?: () => void
}

export function KeptTextNotice({
  heading,
  fields,
  onApplyAgain,
  onDiscard,
  onDismiss,
}: KeptTextNoticeProps) {
  const isOffer = onApplyAgain !== undefined

  return (
    <Card>
      <CardContent className="flex flex-col gap-3 pt-6">
        <p className="text-sm font-medium">{heading}</p>
        {fields.length > 0 && (
          <dl className="flex flex-col gap-1 pl-4 text-sm">
            {fields.map((field) => (
              <div key={field.label} className="flex min-w-0 gap-2">
                <dt className="text-muted-foreground shrink-0">{field.label}:</dt>
                <dd className="min-w-0 select-text">
                  <PlainText text={field.value} />
                </dd>
              </div>
            ))}
          </dl>
        )}
        <div className="flex justify-end gap-2">
          {isOffer ? (
            <>
              <Button type="button" variant="ghost" onClick={onDiscard}>
                Discard
              </Button>
              <Button type="button" onClick={onApplyAgain}>
                Apply again
              </Button>
            </>
          ) : (
            <Button type="button" variant="ghost" onClick={onDismiss}>
              Dismiss
            </Button>
          )}
        </div>
      </CardContent>
    </Card>
  )
}
