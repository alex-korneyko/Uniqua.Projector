import { QueryClientProvider } from '@tanstack/react-query'
import { StrictMode } from 'react'
import { createRoot } from 'react-dom/client'

import App from '@/App'
import { queryClient } from '@/api/queryClient'
import { AccountShell } from '@/features/auth/AccountShell'
import '@/index.css'

// The shell is the outermost decision the client makes: who am I, and therefore what do I show.
// A visitor is handed the sign-in and registration screens; a recognised account gets the product.
createRoot(document.getElementById('root')!).render(
  <StrictMode>
    <QueryClientProvider client={queryClient}>
      <AccountShell>
        <App />
      </AccountShell>
    </QueryClientProvider>
  </StrictMode>,
)
