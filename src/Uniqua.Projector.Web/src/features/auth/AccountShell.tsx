import { useEffect, useRef, type ReactNode } from 'react'
import { Navigate, useInRouterContext, useLocation } from 'react-router'

import { isInAppPath } from '@/app/routes'
import { Button } from '@/components/ui/button'
import { useSession } from '@/features/auth/useSession'
import { clearAll, discardUnlessOwnedBy } from '@/features/boards/draftStore'

interface AccountShellProps {
  /**
   * What a visitor is shown — the sign-in and registration screens. Passed in rather than imported
   * so this shell owns only the decision about which state we are in, and the screens can be
   * tested and built without it.
   */
  children: ReactNode
  /** What a recognised account is shown under the header — the route table, in the app. */
  signedIn?: ReactNode
}

/**
 * Decides which of four things the client is currently looking at, and nothing else.
 *
 * The distinction the whole component exists for is between **visitor** and **error**: not being
 * recognised is the ordinary condition of someone who has not signed in, while a failed call means
 * the server could not answer. Showing a sign-in form for an outage would hide it behind something
 * that looks entirely normal, and invite people to type a password at a server that cannot check it.
 */
export function AccountShell({ children, signedIn }: AccountShellProps) {
  const { state, retry, signOut, isSigningOut, signOutFailed } = useSession()
  const inRouter = useInRouterContext()
  const accountId = state.status === 'account' ? state.account.id : undefined

  // AC-28: text kept by a different account is removed the moment this one is known, never shown.
  useEffect(() => {
    if (accountId !== undefined) {
      discardUnlessOwnedBy(accountId)
    }
  }, [accountId])

  // AC-28: kept text goes with a sign-out that happened — not with one that failed, which leaves
  // the member signed in and still able to apply it.
  const signingOut = useRef(false)
  useEffect(() => {
    if (!signingOut.current) {
      return
    }
    if (state.status === 'visitor') {
      signingOut.current = false
      clearAll()
    } else if (signOutFailed) {
      signingOut.current = false
    }
  }, [state.status, signOutFailed])

  const content = signedIn ?? (
    <p className="text-muted-foreground text-sm">
      You are signed in. The board arrives in a later step.
    </p>
  )

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

  // AC-27: a visitor is shown the same sign-in form at every address, a board's included, and
  // nothing below it renders — so no board request is made. The address itself is left alone: it
  // is the `returnTo` the account is answered at once it signs in.
  if (state.status === 'visitor') {
    return <>{children}</>
  }

  return (
    // Full width: the board (SCR-04) needs every column of room it can get. The screens that read
    // better narrow — the board list (SCR-02) and not-available (SCR-08) — bound themselves.
    <div className="flex min-h-dvh flex-col gap-6 p-8">
      <header className="flex items-center justify-between gap-4 border-b pb-4">
        {/* AC-11: the display name, and never the address — not even as a fallback. */}
        <span className="font-medium">{state.account.display_name}</span>
        <Button
          variant="outline"
          size="sm"
          onClick={() => {
            signingOut.current = true
            signOut()
          }}
          disabled={isSigningOut}
        >
          {isSigningOut ? 'Signing out…' : 'Sign out'}
        </Button>
      </header>

      {signOutFailed && (
        <p role="alert" className="text-destructive text-sm">
          We could not sign you out. You are still signed in — please try again.
        </p>
      )}

      {/* Outside a router (the shell on its own) there is no address to guard. */}
      {inRouter ? <FollowReturnTo>{content}</FollowReturnTo> : content}
    </div>
  )
}

/**
 * The `returnTo` guard, applied once an account is known: the address the visitor arrived at is
 * followed only when it is a path inside the application (sad.md §8). Anything else — `//evil`,
 * which a browser and React Router both parse as a path — lands on the board list.
 */
function FollowReturnTo({ children }: { children: ReactNode }) {
  const { pathname, search, hash } = useLocation()
  return isInAppPath(pathname + search + hash) ? <>{children}</> : <Navigate to="/" replace />
}
