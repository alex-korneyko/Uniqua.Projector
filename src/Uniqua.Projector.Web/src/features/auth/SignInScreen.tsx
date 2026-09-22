import { useMutation, useQueryClient } from '@tanstack/react-query'
import { useState } from 'react'

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
export function SignInScreen() {
  const queryClient = useQueryClient()

  const [email, setEmail] = useState('')
  const [password, setPassword] = useState('')
  const [refusal, setRefusal] = useState<Refusal | null>(null)

  const signIn = useMutation({
    mutationFn: createSession,
    onSuccess: () => {
      setRefusal(null)
      // The cookie is set; the shell only has to re-ask who it is.
      void queryClient.invalidateQueries({ queryKey: sessionQueryKey })
    },
    onError: (error: unknown) => setRefusal(describeRefusal(error)),
  })

  return (
    <main className="mx-auto flex min-h-dvh max-w-md items-center p-4">
      <Card className="w-full">
        <CardHeader>
          <CardTitle>Sign in</CardTitle>
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
        </CardContent>
      </Card>
    </main>
  )
}
