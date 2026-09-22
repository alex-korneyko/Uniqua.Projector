import { Slot } from '@radix-ui/react-slot'
import { type VariantProps } from 'class-variance-authority'
import * as React from 'react'

import { buttonVariants } from '@/components/ui/button-variants'
import { cn } from '@/lib/utils'

// shadcn/ui component source, vendored into the repository rather than installed as a dependency,
// so it can be edited like any other file here (see docs/architecture-map.md § Frontend).
export interface ButtonProps
  extends React.ButtonHTMLAttributes<HTMLButtonElement>,
    VariantProps<typeof buttonVariants> {
  asChild?: boolean
}

export function Button({ className, variant, size, asChild = false, ...props }: ButtonProps) {
  const Comp = asChild ? Slot : 'button'
  return <Comp className={cn(buttonVariants({ variant, size, className }))} {...props} />
}
