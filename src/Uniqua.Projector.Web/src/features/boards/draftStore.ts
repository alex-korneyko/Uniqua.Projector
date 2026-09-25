/**
 * T18 / AC-28, sad.md §8 "Typed text across sign-in". What a member typed into a change that was
 * refused because their session had ended, kept so that the same account can apply it again once
 * it signs back in.
 *
 * `sessionStorage` only: it never outlives the tab, and it never reaches another one. One slot, kept
 * under the id of the account that typed it — offered back to that account alone, and removed when
 * taken, when a different account signs in (never shown), or on sign-out.
 */
export interface KeptDraft {
  /** The board the change was aimed at, when it was aimed at one. */
  boardId?: string
  /** Which change this was — a card title, a column name, and so on. */
  item: string
  /** What the member typed, field by field. */
  fields: Record<string, string>
}

interface StoredDraft {
  accountId: string
  draft: KeptDraft
}

const storageKey = 'uniqua.projector.keptText'

export function keep(accountId: string, draft: KeptDraft): void {
  const stored: StoredDraft = { accountId, draft }
  try {
    sessionStorage.setItem(storageKey, JSON.stringify(stored))
  } catch {
    // Storage refused (full, or disabled): the text is lost exactly as it would be with no store.
  }
}

/** The text kept by this account, removed as it is handed over so it cannot be applied twice. */
export function takeFor(accountId: string): KeptDraft | undefined {
  const stored = read()
  if (stored?.accountId !== accountId) {
    return undefined
  }

  clearAll()
  return stored.draft
}

/** Removes, unseen, text kept by any account other than this one. */
export function discardUnlessOwnedBy(accountId: string): void {
  const stored = read()
  if (stored !== undefined && stored.accountId !== accountId) {
    clearAll()
  }
}

/** Removes every kept draft — on sign-out. */
export function clearAll(): void {
  try {
    sessionStorage.removeItem(storageKey)
  } catch {
    // Nothing could be stored either, so there is nothing to remove.
  }
}

function read(): StoredDraft | undefined {
  let raw: string | null
  try {
    raw = sessionStorage.getItem(storageKey)
  } catch {
    return undefined
  }

  if (raw === null) {
    return undefined
  }

  try {
    const parsed = JSON.parse(raw) as Partial<StoredDraft> | null
    if (typeof parsed?.accountId === 'string' && typeof parsed.draft === 'object' && parsed.draft !== null) {
      return parsed as StoredDraft
    }
  } catch {
    // Unreadable: treated as nothing kept, and removed below so it cannot be misread later.
  }

  clearAll()
  return undefined
}
