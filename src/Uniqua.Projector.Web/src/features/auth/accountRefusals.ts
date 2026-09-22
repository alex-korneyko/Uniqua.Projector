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
 * repositories. The local strings exist only for the cases the contract does not describe — a
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
      return { message: error.detail ?? 'That password is not usable.', field: 'password' }

    case 'accounts.email_invalid':
      return { message: error.detail ?? 'That address is not usable.', field: 'email' }

    case 'accounts.email_taken':
      return { message: error.detail ?? 'That address is already registered.', field: 'email' }

    case 'accounts.display_name_invalid':
      return { message: error.detail ?? 'That display name is not usable.', field: 'displayName' }

    case 'accounts.display_name_taken':
      return {
        message: error.detail ?? 'That display name is already taken.',
        field: 'displayName',
      }

    case 'accounts.registration_rate_limited':
      return { message: rateLimitMessage(error), field: null }

    case 'accounts.antiforgery_failed':
      // Nothing the visitor typed was wrong, so this is not a message about a field.
      return {
        message: 'We could not verify that request. Please try again.',
        field: null,
      }

    case 'accounts.credentials_invalid':
      // AC-05 / AC-05b: one message, naming neither of the two, and no field — highlighting one
      // would answer the question the wording is careful not to.
      return {
        message: error.detail ?? 'The address or the password is incorrect.',
        field: null,
      }

    default:
      // An unmapped status. One plain sentence, and emphatically not the code or the number: those
      // tell a visitor nothing they can act on.
      return { message: 'Something went wrong. Please try again.', field: null }
  }
}

/** AC-01b asks that the visitor be told when they may try again, so the number is shown. */
function rateLimitMessage(error: ApiError): string {
  const detail = error.detail ?? 'Registration is temporarily limited from here.'

  if (error.retryAfterSeconds === undefined || error.retryAfterSeconds <= 0) {
    // The contract says the number is present on this code, but a client that printed
    // "try again in undefined seconds" when it was not would be worse than one that says less.
    return `${detail} Please try again shortly.`
  }

  const unit = error.retryAfterSeconds === 1 ? 'second' : 'seconds'
  return `${detail} Try again in ${error.retryAfterSeconds} ${unit}.`
}
