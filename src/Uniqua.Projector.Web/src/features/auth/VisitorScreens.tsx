import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { RegisterScreen } from '@/features/auth/RegisterScreen'
import { SignInScreen } from '@/features/auth/SignInScreen'

/**
 * What a visitor is shown: one of the two screens, and — inside its card — a way to reach the
 * other.
 *
 * The sign-in form comes first, for everyone (the owner's decision of 2026-09-22, spec §8). Most
 * arrivals already have an account, and AC-07, AC-07b and AC-10 all present the sign-in form to
 * someone whose session ended. A stranger from the public link still reaches registration unaided
 * (AC-01): the way there is inside the same card, one click away.
 */
export function VisitorScreens() {
  const [screen, setScreen] = useState<'signIn' | 'register'>('signIn')

  // N-12: switching between the two screens unmounts the toggle button that had focus, so focus
  // falls to <body> unless something claims it. The landing's first mount is not a switch — only
  // an actual toggle click should move focus, so this tracks that rather than deriving it from
  // `screen`.
  const [hasSwitched, setHasSwitched] = useState(false)

  function switchTo(next: 'signIn' | 'register') {
    setHasSwitched(true)
    setScreen(next)
  }

  return screen === 'signIn' ? (
    <SignInScreen
      autoFocusHeading={hasSwitched}
      alternative={
        <Button variant="ghost" onClick={() => switchTo('register')}>
          No account yet? Create one
        </Button>
      }
    />
  ) : (
    <RegisterScreen
      autoFocusHeading={hasSwitched}
      alternative={
        <Button variant="ghost" onClick={() => switchTo('signIn')}>
          I already have an account
        </Button>
      }
    />
  )
}
