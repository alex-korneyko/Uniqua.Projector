import * as React from 'react'

import { cn } from '@/lib/utils'

// shadcn/ui component source, vendored into the repository rather than installed as a dependency,
// so it can be edited like any other file here (see docs/architecture-map.md § Frontend).
export function Textarea({ className, ...props }: React.ComponentProps<'textarea'>) {
  return (
    <textarea
      className={cn(
        'border-input bg-background ring-offset-background placeholder:text-muted-foreground',
        'focus-visible:ring-ring flex min-h-20 w-full rounded-md border px-3 py-2 text-sm',
        'focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:outline-none',
        'aria-invalid:border-destructive disabled:cursor-not-allowed disabled:opacity-50',
        className,
      )}
      {...props}
    />
  )
}
