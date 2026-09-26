import { useMutation, useQueryClient, type QueryClient } from '@tanstack/react-query'
import { useState } from 'react'

import { ApiError } from '@/api/accounts'
import {
  boardQueryKey,
  cardQueryKey,
  currentStateOf,
  openBoard,
  patchBoardFromCurrent,
  type Board,
  type Card,
} from '@/api/boards'

/**
 * T17. Wraps a board, column or card mutation so every caller answers AC-18b the same way: when a
 * change names something that has since gone (a deleted column or card), the client re-reads the
 * board once, exactly as `boards.not_available` on a change is told apart from a never-existing
 * board, to tell "that card no longer exists" from "that board no longer exists" apart
 * (sad.md §6 flow 2). A 401 is answered as ending the session, and its text is T18's to keep. The
 * text the member typed is never lost: it is returned as `keptText`, whatever the refusal.
 *
 * Every patch goes through the TanStack Query cache (sad.md §8, Client state): the hook holds only
 * the outcome of the last change, never a copy of the board.
 */
export type BoardChangeResolution = 'item-gone' | 'board-gone' | 'session-ended' | undefined

export interface UseBoardChangeOptions<TVariables, TResult> {
  boardId: string
  mutationFn: (variables: TVariables) => Promise<TResult>
  /** Applies an accepted change to the cached board (checklist: patch helpers over `api/boards.ts`). */
  applyAccepted?: (board: Board, result: TResult, variables: TVariables) => Board
  /** The card an accepted change leaves behind, written to that card's cache (an `editCard` answer). */
  acceptedCard?: (result: TResult, variables: TVariables) => Card | undefined
  /**
   * Applies a stale refusal's `current_*` payload to the cached board (edge case row 3). Called only
   * when the refusal carries one. Defaults to `patchBoardFromCurrent`.
   */
  applyRefusalCurrent?: (board: Board, error: ApiError) => Board
}

export interface UseBoardChangeResult<TVariables> {
  change: (variables: TVariables) => void
  isPending: boolean
  error: unknown
  /** Set once a 404 has been told apart from a never-existing board by the one re-read. */
  resolvedAs: BoardChangeResolution
  /** What the member typed, kept after a refusal — never swallowed. */
  keptText: TVariables | undefined
}

const patchFromCurrent = (board: Board, error: ApiError) =>
  patchBoardFromCurrent(board, currentStateOf(error))

export function useBoardChange<TVariables, TResult>({
  boardId,
  mutationFn,
  applyAccepted,
  acceptedCard,
  applyRefusalCurrent = patchFromCurrent,
}: UseBoardChangeOptions<TVariables, TResult>): UseBoardChangeResult<TVariables> {
  const queryClient = useQueryClient()
  const [resolvedAs, setResolvedAs] = useState<BoardChangeResolution>(undefined)
  const [keptText, setKeptText] = useState<TVariables | undefined>(undefined)

  const mutation = useMutation<TResult, unknown, TVariables>({
    mutationFn: async (variables) => {
      try {
        return await mutationFn(variables)
      } catch (error) {
        // Resolved before the mutation settles, so a screen never sees the refusal without knowing
        // which of the three it is.
        setResolvedAs(await resolveRefusal(queryClient, boardId, error))
        throw error
      }
    },
    onMutate: () => {
      setResolvedAs(undefined)
    },
    onSuccess: (result, variables) => {
      setKeptText(undefined)

      if (applyAccepted !== undefined) {
        patchCachedBoard(queryClient, boardId, (board) => applyAccepted(board, result, variables))
      }

      const card = acceptedCard?.(result, variables)
      if (card !== undefined) {
        queryClient.setQueryData(cardQueryKey(boardId, card.id), card)
      }
    },
    onError: (error, variables) => {
      setKeptText(variables)

      if (!(error instanceof ApiError)) {
        return
      }

      const current = currentStateOf(error)
      const carriesCurrent = Object.values(current).some((member) => member !== undefined)
      if (!carriesCurrent) {
        return
      }

      patchCachedBoard(queryClient, boardId, (board) => applyRefusalCurrent(board, error))

      if (current.current_card !== undefined) {
        queryClient.setQueryData(cardQueryKey(boardId, current.current_card.id), current.current_card)
      }
    },
  })

  return {
    change: mutation.mutate,
    isPending: mutation.isPending,
    error: mutation.error ?? undefined,
    resolvedAs,
    keptText,
  }
}

/** Patches the cached board if there is one; with none cached there is nothing to patch. */
function patchCachedBoard(
  queryClient: QueryClient,
  boardId: string,
  patch: (board: Board) => Board,
): void {
  queryClient.setQueryData<Board>(boardQueryKey(boardId), (board) =>
    board === undefined ? undefined : patch(board),
  )
}

async function resolveRefusal(
  queryClient: QueryClient,
  boardId: string,
  error: unknown,
): Promise<BoardChangeResolution> {
  if (!(error instanceof ApiError)) {
    return undefined
  }

  if (error.status === 401) {
    return 'session-ended'
  }

  if (error.status === 404) {
    return reReadBoard(queryClient, boardId)
  }

  return undefined
}

/**
 * The one re-read (sad.md §6 flow 2): a gone column or card and a gone board answer a change with
 * the same `boards.not_available`, and only the board's own answer tells them apart. A board that
 * answers refreshes the cache, so the screen shows what is there now.
 */
async function reReadBoard(queryClient: QueryClient, boardId: string): Promise<BoardChangeResolution> {
  const key = boardQueryKey(boardId)

  try {
    await queryClient.fetchQuery({ queryKey: key, queryFn: () => openBoard(boardId), staleTime: 0, retry: false })
    return 'item-gone'
  } catch (reReadError) {
    if (reReadError instanceof ApiError && reReadError.status === 404) {
      // Gone, or no longer ours: its cached copy must not outlive it.
      queryClient.removeQueries({ queryKey: key })
      return 'board-gone'
    }

    // No answer about the board. Nothing says it is gone, so a member still looking at it stays on it
    // (with what they typed); with no board cached there is nothing to stay on.
    return queryClient.getQueryData(key) === undefined ? 'board-gone' : 'item-gone'
  }
}
