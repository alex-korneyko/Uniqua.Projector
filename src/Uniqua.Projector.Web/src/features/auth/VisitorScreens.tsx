import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { RegisterScreen } from '@/features/auth/RegisterScreen'
import { SignInScreen } from '@/features/auth/SignInScreen'

interface VisitorScreensProps {
  /**
   * Whether this visitor's session on this client ended, as the shell knows it. Passed in rather
   * than read from the session query here: a second observer mounting on a refused query would
   * refetch it, flip the shell back to "checking", and unmount this very component.
   */
  ended?: boolean
}

/**
 * What a visitor is shown: one of the two screens, and — inside its card — a way to reach the
 * other.
 *
 * Which comes first depends on who the visitor is. Someone whose session ended (signed out,
 * expired, revoked) already owns an account, and AC-07, AC-07b and AC-10 say they are presented
 * the sign-in form. A stranger arriving on the public link is offered registration first, because
 * AC-01's whole point is that they can reach the product unaided and the sign-in form asks a
 * question they cannot yet answer.
 */
export function VisitorScreens({ ended = false }: VisitorScreensProps) {
  const [screen, setScreen] = useState<'register' | 'signIn'>(ended ? 'signIn' : 'register')

  return screen === 'register' ? (
    <RegisterScreen
      alternative={
        <Button variant="ghost" onClick={() => setScreen('signIn')}>
          I already have an account
        </Button>
      }
    />
  ) : (
    <SignInScreen
      alternative={
        <Button variant="ghost" onClick={() => setScreen('register')}>
          Create an account instead
        </Button>
      }
    />
  )
}
