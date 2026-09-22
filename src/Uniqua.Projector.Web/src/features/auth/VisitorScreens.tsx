import { useState } from 'react'

import { Button } from '@/components/ui/button'
import { RegisterScreen } from '@/features/auth/RegisterScreen'
import { SignInScreen } from '@/features/auth/SignInScreen'

/**
 * What a visitor is shown: one of the two screens, and a way to reach the other.
 *
 * Registration is offered first. AC-01's whole point is that a stranger arriving on a public link
 * can reach the product unaided, and putting the sign-in form in front of them asks a question
 * they cannot yet answer.
 */
export function VisitorScreens() {
  const [screen, setScreen] = useState<'register' | 'signIn'>('register')

  return (
    <div className="flex flex-col items-center">
      {screen === 'register' ? <RegisterScreen /> : <SignInScreen />}

      <Button
        variant="ghost"
        className="mb-8"
        onClick={() => setScreen(screen === 'register' ? 'signIn' : 'register')}
      >
        {screen === 'register' ? 'I already have an account' : 'Create an account instead'}
      </Button>
    </div>
  )
}
