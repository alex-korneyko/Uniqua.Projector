import { MutationCache, QueryCache, QueryClient } from '@tanstack/react-query'

import { ApiError } from '@/api/accounts'

/** The cache key for "who am I". One key, so there is exactly one answer on the client. */
export const sessionQueryKey = ['session'] as const

/** The code the server answers with when a request's session is not (or no longer) recognised. */
const sessionNotRecognised = 'accounts.session_not_recognised'

/**
 * TanStack Query owns all server state. A push event from the realtime hub invalidates or patches
 * the cached board rather than maintaining a second copy of it, so there is exactly one answer to
 * "what does this board look like" on the client.
 *
 * Every query and mutation shares one rule: a refusal saying the session is not recognised ends
 * the session on the client, whichever call received it (AC-10: "refuses ... regardless of what
 * their browser still holds"). It keys on the code, not the status — a wrong password at sign-in
 * is also a 401, and it is a refusal on a form, not the end of anything.
 */
export function createQueryClient(): QueryClient {
  const client: QueryClient = new QueryClient({
    queryCache: new QueryCache({ onError: (error) => endSessionIfUnrecognised(client, error) }),
    mutationCache: new MutationCache({
      onError: (error) => endSessionIfUnrecognised(client, error),
    }),
    defaultOptions: {
      queries: { staleTime: 30_000, refetchOnWindowFocus: false },
    },
  })

  return client
}

export const queryClient = createQueryClient()

function endSessionIfUnrecognised(client: QueryClient, error: unknown) {
  if (
    error instanceof ApiError
    && error.code === sessionNotRecognised
    && client.getQueryData(sessionQueryKey) != null
  ) {
    // null, not removed: null is "known to be signed out", so the shell shows the visitor screens
    // at once instead of checking again first.
    client.setQueryData(sessionQueryKey, null)
  }
}
