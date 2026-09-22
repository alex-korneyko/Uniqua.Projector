import { QueryClient } from '@tanstack/react-query'

/**
 * TanStack Query owns all server state. A push event from the realtime hub invalidates or patches
 * the cached board rather than maintaining a second copy of it, so there is exactly one answer to
 * "what does this board look like" on the client.
 */
export const queryClient = new QueryClient({
  defaultOptions: {
    queries: { staleTime: 30_000, refetchOnWindowFocus: false },
  },
})
