import { ApiError } from '@/api/accounts'

/** Which field a refusal was about, so focus can land where the correction has to be made. */
export type AccountField = 'email' | 'password' | 'displayName' | null

export interface Refusal {
  /** What the visitor is shown. One message, never a raw code and never a bare status. */
  readonly message: string
  /** The field to focus, or null when nothing the visitor typed was wrong. */
  readonly field: AccountField
}

/**
 * Turns whatever came back into one sentence and, where there is one, the field it concerns.
 *
 * The server's wording is preferred wherever it sent some: it is the contract's, it is the one
 * place refusals are worded, and paraphrasing it here would put the same sentence in two
 * repositories. The problem's title is the plain statement an acceptance criterion asks for
 * ("That address is already registered", AC-03) and its detail is the reason, so both are shown,
 * statement first. The local strings exist only for the cases the contract does not describe — a
 * gateway answering instead of the application, or no answer at all.
 */
export function describeRefusal(error: unknown): Refusal {
  if (!(error instanceof ApiError)) {
    // A network failure. The request may or may not have reached the server, so nothing is claimed
    // about whether the account now exists — saying either would be a guess presented as fact.
    return {
      message: 'We could not reach the server. Please check your connection and try again.',
      field: null,
    }
  }

  switch (error.code) {
    case 'accounts.password_invalid':
      return { message: statementAndReason(error, 'That password is not usable.'), field: 'password' }

    case 'accounts.email_invalid':
      return { message: statementAndReason(error, 'That address is not usable.'), field: 'email' }

    case 'accounts.email_taken':
      return { message: statementAndReason(error, 'That address is already registered.'), field: 'email' }

    case 'accounts.display_name_invalid':
      return { message: statementAndReason(error, 'That display name is not usable.'), field: 'displayName' }

    case 'accounts.display_name_taken':
      return {
        message: statementAndReason(error, 'That display name is already taken.'),
        field: 'displayName',
      }

    case 'accounts.registration_rate_limited':
      return { message: rateLimitMessage(error, 'Registration is temporarily limited from here.'), field: null }

    case 'accounts.sign_in_rate_limited':
      // AC-12: the cap on failed sign-ins per source. Its own statement and reason, never the
      // credentials wording — a rate limit is not a claim about what was typed — and the form
      // stays usable: the visitor may still retry once the wait is up, or from another source.
      return { message: rateLimitMessage(error, 'Sign-in is temporarily limited from here.'), field: null }

    case 'accounts.antiforgery_failed':
      // Nothing the visitor typed was wrong, so this is not a message about a field.
      return {
        message: withRetry(statementAndReason(error, 'We could not verify that request.')),
        field: null,
      }

    case 'accounts.credentials_invalid':
      // AC-05 / AC-05b: one message, naming neither of the two, and no field — highlighting one
      // would answer the question the wording is careful not to.
      return {
        message: statementAndReason(error, 'The address or the password is incorrect.'),
        field: null,
      }

    default:
      // An unmapped status. One plain sentence, and emphatically not the code or the number: those
      // tell a visitor nothing they can act on.
      return { message: 'Something went wrong. Please try again.', field: null }
  }
}

/**
 * The title as the statement, then the detail as the reason — or whichever of the two the server
 * sent. A detail that already begins with the title is shown alone rather than saying it twice.
 */
function statementAndReason(error: ApiError, fallback: string): string {
  const title = error.title?.trim()
  const detail = error.detail?.trim()

  if (!title) {
    return detail || fallback
  }

  if (!detail) {
    return asSentence(title)
  }

  return detail.toLowerCase().startsWith(title.toLowerCase())
    ? detail
    : `${asSentence(title)} ${detail}`
}

function asSentence(text: string): string {
  return /[.!?]$/.test(text) ? text : `${text}.`
}

/** Adds the plain retry, unless the message already tells the visitor to try again. */
function withRetry(message: string): string {
  return /try again/i.test(message) ? message : `${message} Please try again.`
}

/**
 * AC-01b / AC-12 both ask that the visitor be told when they may try again, so the number is
 * shown — once, whichever of the two rate limits sent it.
 */
function rateLimitMessage(error: ApiError, fallback: string): string {
  const statement = statementAndReason(error, fallback)

  if (/try again/i.test(statement)) {
    // The server already named the wait; a second sentence would say it twice.
    return statement
  }

  if (error.retryAfterSeconds === undefined || error.retryAfterSeconds <= 0) {
    // The contract says the number is present on this code, but a client that printed
    // "try again in undefined seconds" when it was not would be worse than one that says less.
    return `${statement} Please try again shortly.`
  }

  const unit = error.retryAfterSeconds === 1 ? 'second' : 'seconds'
  return `${statement} Try again in ${error.retryAfterSeconds} ${unit}.`
}
