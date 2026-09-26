import { QueryClientProvider } from '@tanstack/react-query'
import { BrowserRouter, useRoutes } from 'react-router'

import { queryClient } from '@/api/queryClient'
import { appRoutes } from '@/app/routes'
import { AccountShell } from '@/features/auth/AccountShell'
import { VisitorScreens } from '@/features/auth/VisitorScreens'

/**
 * The client, composed. The shell is the outermost decision it makes — who am I, and therefore what
 * do I show: a visitor is handed the sign-in and registration screens at whatever address they
 * arrived on, and a recognised account gets the route table (ADR 0012).
 */
export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <BrowserRouter>
        <AccountShell signedIn={<AppRoutes />}>
          <VisitorScreens />
        </AccountShell>
      </BrowserRouter>
    </QueryClientProvider>
  )
}

function AppRoutes() {
  return useRoutes(appRoutes)
}
