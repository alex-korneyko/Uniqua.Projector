import { ApiError } from '@/api/accounts'
import { rateLimitMessage, statementAndReason, withRetry } from '@/features/auth/accountRefusals'

/**
 * T17. Turns a board refusal into the one sentence a screen shows, the same way
 * `accountRefusals.ts`' `describeRefusal` does: the server's `title` then its `detail`, and a local
 * sentence only for the four cases the contract does not describe (screens.md §Source, Refusal
 * wording).
 *
 * Two of the four client texts — "That column no longer exists." and "That card no longer
 * exists." — are not a direct reading of any one refusal: they are chosen by `useBoardChange` after
 * it re-reads the board to tell a gone column or card from a gone board apart (AC-18b, sad.md §6
 * flow 2), and are passed in here as `resolvedAs`, with `gone` naming which of the two it was.
 */
export type ResolvedAs = 'item-gone' | 'board-gone' | 'session-ended' | undefined

/** What a change named, for the one refusal whose wording depends on it (AC-18b). */
export type GoneItem = 'column' | 'card'

const unreachable = 'We could not reach the server. Please check your connection and try again.'
const somethingWentWrong = 'Something went wrong. Please try again.'
const goneItemText: Record<GoneItem, string> = {
  column: 'That column no longer exists.',
  card: 'That card no longer exists.',
}

/**
 * The codes a screen has a row for, shown as the server words them. Anything else — the codes a
 * correct client never provokes (OQ-API-2/3) and any code this client does not know — is the one
 * generic sentence, never a raw code or a bare status.
 */
const codesWithARow = new Set([
  'accounts.session_not_recognised',
  'boards.not_available',
  'boards.owner_only',
  'boards.owned_board_limit_reached',
  'boards.board_name_invalid',
  'boards.confirmation_mismatch',
  'boards.column_name_invalid',
  'boards.column_limit_reached',
  'boards.column_renamed',
  'boards.columns_changed',
  'boards.column_not_empty',
  'boards.last_column',
  'boards.card_title_invalid',
  'boards.card_description_invalid',
  'boards.card_limit_reached',
  'boards.card_changed',
])

const codesWithAWait = new Set(['boards.change_rate_limited', 'boards.contended'])

export function describeBoardRefusal(
  error: unknown,
  resolvedAs?: ResolvedAs,
  gone: GoneItem = 'card',
): string {
  if (!(error instanceof ApiError)) {
    // No answer at all: whether the change reached the server is unknown, so nothing is claimed.
    return unreachable
  }

  if (resolvedAs === 'item-gone') {
    return goneItemText[gone]
  }

  const code = error.code ?? ''

  if (codesWithAWait.has(code)) {
    return rateLimitMessage(error, somethingWentWrong)
  }

  if (code === 'accounts.antiforgery_failed') {
    return withRetry(statementAndReason(error, somethingWentWrong))
  }

  return codesWithARow.has(code) ? statementAndReason(error, somethingWentWrong) : somethingWentWrong
}
