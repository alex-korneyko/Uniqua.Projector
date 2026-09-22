import * as React from 'react'

import { cn } from '@/lib/utils'

// shadcn/ui component source, vendored into the repository rather than installed as a dependency,
// so it can be edited like any other file here (see docs/architecture-map.md § Frontend).
export function Label({ className, ...props }: React.ComponentProps<'label'>) {
  return (
    <label
      className={cn('text-sm leading-none font-medium select-none', className)}
      {...props}
    />
  )
}
