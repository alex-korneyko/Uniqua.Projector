import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query'

import { ApiError, deleteCurrentSession, getCurrentAccount, type Account } from '@/api/accounts'
import { sessionQueryKey } from '@/api/queryClient'

export { sessionQueryKey }


/**
 * What the client currently knows about who it is.
 *
 * `visitor` and `failed` are deliberately separate. Being unrecognised is the ordinary state of
 * someone who has not signed in, while a failed call means the server could not answer — and a
 * client that showed a sign-in form for an outage would hide it behind something that looks like
 * a normal prompt, inviting people to type a password at a server that cannot check it.
 */
export type SessionState =
  | { status: 'loading' }
  | {
      status: 'visitor'
      /**
       * True when this visitor had a session on this client and it ended — signed out, expired or
       * revoked. They already own an account, so AC-07, AC-07b and AC-10 show them the sign-in
       * form; a first-time arrival is shown registration (AC-01).
       */
      ended: boolean
    }
  | { status: 'account'; account: Account }
  | { status: 'failed' }

/**
 * TanStack Query owns this, as it owns all server state (architecture-map § Frontend). There is no
 * second copy of the account in another store, so nothing can disagree about whether we are signed
 * in.
 */
export function useSession() {
  const queryClient = useQueryClient()

  const query = useQuery({
    queryKey: sessionQueryKey,
    queryFn: getCurrentAccount,
    // A 401 is an answer, not a failure: it means "you are a visitor". Retrying it would delay the
    // sign-in form for every first-time arrival.
    retry: (failureCount, error) =>
      error instanceof ApiError && error.isNotRecognised ? false : failureCount < 2,
  })

  const signOut = useMutation({
    mutationFn: deleteCurrentSession,
    onSuccess: () => {
      // Forgotten rather than refetched, so the visitor view appears at once (AC-08) instead of
      // after a round trip that can only confirm what we already know.
      queryClient.setQueryData(sessionQueryKey, null)
      void queryClient.invalidateQueries({ queryKey: sessionQueryKey })
    },
    onError: (error) => {
      // A 401 means the session was already gone. The end state is the one that was asked for, so
      // it is a success — anything else would report a failure for having succeeded twice.
      if (error instanceof ApiError && error.isNotRecognised) {
        queryClient.setQueryData(sessionQueryKey, null)
        void queryClient.invalidateQueries({ queryKey: sessionQueryKey })
      }
    },
  })

  return {
    state: toState(query),
    /** Retries the bootstrap call after a failure that was not a refusal. */
    retry: () => void query.refetch(),
    signOut: () => signOut.mutate(),
    /**
     * True only when the sign-out call itself failed for a reason other than the session already
     * being gone. Claiming a sign-out that did not happen is the one wrong answer available here.
     */
    signOutFailed:
      signOut.isError
      && !(signOut.error instanceof ApiError && signOut.error.isNotRecognised),
  }
}

function toState(query: {
  isPending: boolean
  isFetching: boolean
  data: Account | null | undefined
  error: unknown
}): SessionState {
  // Before the cached account: a refetch that is refused keeps the previous data, and showing it
  // would present an ended session as a live one (AC-10).
  if (query.error instanceof ApiError && query.error.isNotRecognised) {
    return { status: 'visitor', ended: query.data !== undefined }
  }

  if (query.data != null) {
    return { status: 'account', account: query.data }
  }

  if (query.error != null) {
    return { status: 'failed' }
  }

  // data === null is a signed-out account: known, not pending.
  return query.data === null ? { status: 'visitor', ended: true } : { status: 'loading' }
}
