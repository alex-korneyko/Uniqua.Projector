import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type ReactNode } from 'react'

import { createSession } from '@/api/accounts'
import { Button } from '@/components/ui/button'
import {
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
} from '@/components/ui/card'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { describeRefusal, type Refusal } from '@/features/auth/accountRefusals'
import { sessionQueryKey } from '@/features/auth/useSession'
import { discardUnlessOwnedBy } from '@/features/boards/draftStore'

/**
 * AC-04: how an account that comes back gets a session.
 *
 * This screen is defined by what it does not do. The server goes to real trouble to make a wrong
 * password and an unregistered address indistinguishable — one code, one sentence, and a full
 * dummy password verification so the two take comparable time. Every client-side shortcut would
 * give that away again:
 *
 * - validating the address shape would refuse instantly what the server refuses slowly, and the
 *   difference in speed answers "is this address registered?";
 * - a minimum password length would reveal that a rejection was about length rather than about
 *   the credential;
 * - marking either field invalid on a refusal would say which of the two was wrong.
 *
 * So there are no hints, no patterns, no field-level errors here — unlike the registration screen,
 * where bounds are genuinely helpful because nothing is being concealed.
 */
interface SignInScreenProps {
  /** Shown inside the card, under the form — the way to create an account instead, where it cannot fall below the fold. */
  alternative?: ReactNode
  /**
   * True when this screen replaced the other one under the visitor's hands (N-12), so focus
   * should move to its heading. False on the landing's first mount, where nothing was switched
   * away from and focus should stay wherever the browser already put it.
   */
  autoFocusHeading?: boolean
}

export function SignInScreen({ alternative, autoFocusHeading = false }: SignInScreenProps = {}) {
  const queryClient = useQueryClient()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [refusal, setRefusal] = useState<Refusal | null>(null)

  const headingRef = useRef<HTMLHeadingElement>(null)

  // N-12: claim focus on mount, but only when this mount is the result of switching from the
  // registration screen. The landing's first mount leaves focus alone.
  useEffect(() => {
    if (autoFocusHeading) {
      headingRef.current?.focus()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- runs once, for this mount only
  }, [])

  const signIn = useMutation({
    mutationFn: createSession,
    onSuccess: (account) => {
      setRefusal(null)
      // AC-28: text another account kept in this tab goes before this one is shown anything.
      discardUnlessOwnedBy(account.id)
      // The cookie is set and the 201 carries the account, so the session is known at once rather
      // than after /me answers; the re-check still runs, to confirm with the server.
      queryClient.setQueryData(sessionQueryKey, account)
      void queryClient.invalidateQueries({ queryKey: sessionQueryKey })
    },
    onError: (error: unknown) => setRefusal(describeRefusal(error)),
  })

  return (
    <main className="mx-auto flex min-h-dvh max-w-md items-center p-4">
      <Card className="w-full">
        <CardHeader>
          <CardTitle ref={headingRef} tabIndex={-1}>
            Sign in
          </CardTitle>
          <CardDescription>Welcome back.</CardDescription>
        </CardHeader>

        <CardContent>
          <form
            noValidate
            className="flex flex-col gap-4"
            onSubmit={(event) => {
              event.preventDefault()
              setRefusal(null)
              // Submitted as typed, whatever it looks like. The server is the only judge of
              // whether these are credentials, and it answers every guess at the same cost.
              signIn.mutate({ email, password })
            }}
          >
            <div className="flex flex-col gap-2">
              <Label htmlFor="sign-in-email">Email address</Label>
              <Input
                id="sign-in-email"
                type="email"
                autoComplete="email"
                value={email}
                onChange={(event) => setEmail(event.target.value)}
              />
            </div>

            <div className="flex flex-col gap-2">
              <Label htmlFor="sign-in-password">Password</Label>
              <Input
                id="sign-in-password"
                type="password"
                autoComplete="current-password"
                value={password}
                onChange={(event) => setPassword(event.target.value)}
              />
            </div>

            {refusal !== null && (
              // One message, and no field marked. Whatever was wrong, this is all that is said.
              <p role="alert" className="text-destructive text-sm">
                {refusal.message}
              </p>
            )}

            <Button type="submit" disabled={signIn.isPending}>
              {signIn.isPending ? 'Signing in…' : 'Sign in'}
            </Button>
          </form>

          {alternative !== undefined && <div className="mt-4 flex justify-center">{alternative}</div>}
        </CardContent>
      </Card>
    </main>
  )
}
