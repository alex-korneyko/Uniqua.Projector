import { useNavigate } from 'react-router'

import { Button } from '@/components/ui/button'
import { Card, CardContent, CardDescription, CardHeader, CardTitle } from '@/components/ui/card'

/**
 * SCR-08 — rendered at the board's own address in place of SCR-04. One answer for a board the
 * account is not a member of, a board that was deleted and a board that never existed (AC-25,
 * AC-20): it takes no props, so it cannot carry a name, an id or a hint of which case applies, and
 * it leaves `document.title` alone.
 */
export function BoardNotAvailable() {
  const navigate = useNavigate()

  return (
    <main className="flex flex-col">
      <Card>
        <CardHeader>
          <CardTitle>Board not available</CardTitle>
          <CardDescription>This board does not exist, or you are not a member of it.</CardDescription>
        </CardHeader>
        <CardContent>
          <Button onClick={() => void navigate('/')}>Back to my boards</Button>
        </CardContent>
      </Card>
    </main>
  )
}
