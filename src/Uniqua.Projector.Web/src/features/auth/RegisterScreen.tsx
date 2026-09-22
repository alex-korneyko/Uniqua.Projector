import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useEffect, useRef, useState, type ReactNode } from 'react'

import { registerAccount } from '@/api/accounts'
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

/** The contract's bounds, offered as help. The server's refusal is always the authority. */
const passwordMinLength = 8
const passwordMaxLength = 128
const displayNameMaxLength = 50

/**
 * AC-01: what a stranger arriving on the public link fills in.
 *
 * The rule the whole screen is built around is that a refusal costs the visitor nothing they
 * typed — including the password. Retyping three fields because one collided is the friction
 * KPI 1 measures, and clearing a password field on error is the most common way to cause it.
 */
interface RegisterScreenProps {
  /** Shown inside the card, under the form — the way to sign in instead, where it cannot fall below the fold. */
  alternative?: ReactNode
  /**
   * True when this screen replaced the other one under the visitor's hands (N-12), so focus
   * should move to its heading. False on the landing's first mount, where nothing was switched
   * away from and focus should stay wherever the browser already put it.
   */
  autoFocusHeading?: boolean
}

export function RegisterScreen({ alternative, autoFocusHeading = false }: RegisterScreenProps = {}) {
  const queryClient = useQueryClient()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [displayName, setDisplayName] = useState('')
  const [refusal, setRefusal] = useState<Refusal | null>(null)

  const headingRef = useRef<HTMLHeadingElement>(null)
  const emailRef = useRef<HTMLInputElement>(null)
  const passwordRef = useRef<HTMLInputElement>(null)
  const displayNameRef = useRef<HTMLInputElement>(null)

  // N-12: claim focus on mount, but only when this mount is the result of switching from the
  // sign-in screen. The landing's first mount leaves focus alone.
  useEffect(() => {
    if (autoFocusHeading) {
      headingRef.current?.focus()
    }
    // eslint-disable-next-line react-hooks/exhaustive-deps -- runs once, for this mount only
  }, [])

  const register = useMutation({
    mutationFn: registerAccount,
    onSuccess: (account) => {
      setRefusal(null)
      // AC-01: the cookie is already set and the 201 carries the whole account, so the session is
      // known at once. Waiting for /me instead would leave the shell reading "visitor" — and this
      // form live — until it answered. The re-check still runs, to confirm with the server.
      queryClient.setQueryData(sessionQueryKey, account)
      void queryClient.invalidateQueries({ queryKey: sessionQueryKey })
    },
    onError: (error: unknown) => setRefusal(describeRefusal(error)),
  })

  // Focus follows the refusal, so a correction does not begin with hunting for which of three
  // fields the server meant.
  useEffect(() => {
    if (refusal === null) {
      return
    }

    // A refusal about nothing the visitor typed moves no focus at all.
    switch (refusal.field) {
      case 'email':
        emailRef.current?.focus()
        break
      case 'password':
        passwordRef.current?.focus()
        break
      case 'displayName':
        displayNameRef.current?.focus()
        break
    }
  }, [refusal])

  return (
    <main className="mx-auto flex min-h-dvh max-w-md items-center p-4">
      <Card className="w-full">
        <CardHeader>
          <CardTitle ref={headingRef} tabIndex={-1}>
            Create an account
          </CardTitle>
          <CardDescription>No invitation needed — this takes one step.</CardDescription>
        </CardHeader>

        <CardContent>
          <form
            noValidate
            className="flex flex-col gap-4"
            onSubmit={(event) => {
              event.preventDefault()
              setRefusal(null)
              register.mutate({ email, password, display_name: displayName })
            }}
          >
            <div className="flex flex-col gap-2">
              <Label htmlFor="register-email">Email address</Label>
              <Input
                id="register-email"
                ref={emailRef}
                type="email"
                autoComplete="email"
                value={email}
                aria-invalid={refusal?.field === 'email'}
                onChange={(event) => setEmail(event.target.value)}
              />
            </div>

            <div className="flex flex-col gap-2">
              <Label htmlFor="register-password">Password</Label>
              <Input
                id="register-password"
                ref={passwordRef}
                type="password"
                autoComplete="new-password"
                value={password}
                minLength={passwordMinLength}
                aria-invalid={refusal?.field === 'password'}
                aria-describedby="register-password-hint"
                onChange={(event) => setPassword(event.target.value)}
              />
              <p id="register-password-hint" className="text-muted-foreground text-xs">
                Between {passwordMinLength} and {passwordMaxLength} characters.
              </p>
            </div>

            <div className="flex flex-col gap-2">
              <Label htmlFor="register-display-name">Display name</Label>
              <Input
                id="register-display-name"
                ref={displayNameRef}
                autoComplete="nickname"
                value={displayName}
                maxLength={displayNameMaxLength}
                aria-invalid={refusal?.field === 'displayName'}
                aria-describedby="register-display-name-hint"
                onChange={(event) => setDisplayName(event.target.value)}
              />
              <p id="register-display-name-hint" className="text-muted-foreground text-xs">
                What other members see. At most {displayNameMaxLength} characters.
              </p>
            </div>

            {refusal !== null && (
              // One message, whatever happened. The screen never adds a second of its own.
              <p role="alert" className="text-destructive text-sm">
                {refusal.message}
              </p>
            )}

            <Button type="submit" disabled={register.isPending}>
              {register.isPending ? 'Creating account…' : 'Create account'}
            </Button>
          </form>

          {alternative !== undefined && <div className="mt-4 flex justify-center">{alternative}</div>}
        </CardContent>
      </Card>
    </main>
  )
}
