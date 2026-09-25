import { cn } from '@/lib/utils'

// AC-16 in one place: whatever a member typed is shown as exactly those characters. The text is a
// single React text node — never parsed as HTML, never linkified — and pre-wrap keeps line breaks
// and repeated spaces as typed, while break-words wraps a long unbroken word inside its container.
export interface PlainTextProps {
  text: string
  as?: 'span' | 'p' | 'div'
  className?: string
}

export function PlainText({ text, as: Tag = 'span', className }: PlainTextProps) {
  return <Tag className={cn('whitespace-pre-wrap break-words', className)}>{text}</Tag>
}
