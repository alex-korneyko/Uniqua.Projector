import type { ReactNode } from 'react'

import { Button } from '@/components/ui/button'
import { useSession } from '@/features/auth/useSession'

interface AccountShellProps {
  /**
   * What a visitor is shown — the sign-in and registration screens. Passed in rather than imported
   * so this shell owns only the decision about which state we are in, and the screens can be
   * tested and built without it.
   */
  children: ReactNode
}

/**
 * Decides which of four things the client is currently looking at, and nothing else.
 *
 * The distinction the whole component exists for is between **visitor** and **error**: not being
 * recognised is the ordinary condition of someone who has not signed in, while a failed call means
 * the server could not answer. Showing a sign-in form for an outage would hide it behind something
 * that looks entirely normal, and invite people to type a password at a server that cannot check it.
 */
export function AccountShell({ children }: AccountShellProps) {
  const { state, retry, signOut, signOutFailed } = useSession()

  if (state.status === 'loading') {
    return (
      <div
        role="status"
        aria-live="polite"
        className="mx-auto flex min-h-dvh max-w-md items-center justify-center p-8"
      >
        <span className="text-muted-foreground text-sm">Checking your session…</span>
      </div>
    )
  }

  if (state.status === 'failed') {
    return (
      <div className="mx-auto flex min-h-dvh max-w-md flex-col items-start justify-center gap-4 p-8">
        <div role="alert" className="flex flex-col gap-1">
          <h1 className="text-lg font-semibold">We could not reach the server</h1>
          <p className="text-muted-foreground text-sm">
            Your session may well still be fine — we simply could not ask.
          </p>
        </div>
        <Button onClick={retry}>Try again</Button>
      </div>
    )
  }

  if (state.status === 'visitor') {
    return <>{children}</>
  }

  return (
    <div className="mx-auto flex min-h-dvh max-w-md flex-col gap-6 p-8">
      <header className="flex items-center justify-between gap-4 border-b pb-4">
        {/* AC-11: the display name, and never the address — not even as a fallback. */}
        <span className="font-medium">{state.account.display_name}</span>
        <Button variant="outline" size="sm" onClick={signOut}>
          Sign out
        </Button>
      </header>

      {signOutFailed && (
        <p role="alert" className="text-destructive text-sm">
          We could not sign you out. You are still signed in — please try again.
        </p>
      )}

      <p className="text-muted-foreground text-sm">
        You are signed in. The board arrives in a later step.
      </p>
    </div>
  )
}
