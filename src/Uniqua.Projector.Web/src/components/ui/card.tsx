import * as React from 'react'

import { cn } from '@/lib/utils'

// shadcn/ui component source, vendored into the repository rather than installed as a dependency,
// so it can be edited like any other file here (see docs/architecture-map.md § Frontend).
export function Card({ className, ...props }: React.ComponentProps<'div'>) {
  return (
    <div
      className={cn('bg-card text-card-foreground rounded-lg border shadow-sm', className)}
      {...props}
    />
  )
}

export function CardHeader({ className, ...props }: React.ComponentProps<'div'>) {
  return <div className={cn('flex flex-col gap-1.5 p-6', className)} {...props} />
}

export function CardTitle({ className, ...props }: React.ComponentProps<'h2'>) {
  return <h2 className={cn('text-xl leading-none font-semibold', className)} {...props} />
}

export function CardDescription({ className, ...props }: React.ComponentProps<'p'>) {
  return <p className={cn('text-muted-foreground text-sm', className)} {...props} />
}

export function CardContent({ className, ...props }: React.ComponentProps<'div'>) {
  return <div className={cn('p-6 pt-0', className)} {...props} />
}
