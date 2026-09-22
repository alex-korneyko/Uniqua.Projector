import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'

import { queryClient } from '@/api/queryClient'
import { AccountShell } from '@/features/auth/AccountShell'
import { VisitorScreens } from '@/features/auth/VisitorScreens'
import '@/index.css'

// The shell is the outermost decision the client makes: who am I, and therefore what do I show.
// A visitor is handed the registration and sign-in screens; a recognised account gets the product.
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AccountShell>{(visitor) => <VisitorScreens ended={visitor.ended} />}</AccountShell>
    </QueryClientProvider>
  </StrictMode>,
)
