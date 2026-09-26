import { beforeEach, describe, expect, it } from 'vitest'

import { clearAll, discardUnlessOwnedBy, keep, takeFor } from '@/features/boards/draftStore'

/**
 * T18 / AC-28, sad.md §8 "Typed text across sign-in" — what a member typed survives a session that
 * ended mid-change, kept in `sessionStorage` under the id of the account that typed it, offered
 * back only to that same account, discarded unseen for a different one, and removed once it has
 * been taken or on sign-out. It never outlives the tab, which `sessionStorage` gives for free.
 */

const accountA = 'account-a'
const accountB = 'account-b'

const draft = { boardId: 'board-1', item: 'card-title', fields: { title: 'Ship the release' } }

beforeEach(() => {
  sessionStorage.clear()
  localStorage.clear()
})

describe('keep + takeFor', () => {
  it('offers kept text back to the account that typed it', () => {
    keep(accountA, draft)

    expect(takeFor(accountA)).toEqual(draft)
  })

  it('never offers kept text to a different account', () => {
    keep(accountA, draft)

    expect(takeFor(accountB)).toBeUndefined()
  })

  it('removes the text once it has been taken, so it cannot be applied twice', () => {
    keep(accountA, draft)

    takeFor(accountA)

    expect(takeFor(accountA)).toBeUndefined()
  })

  it('returns undefined when nothing was kept', () => {
    expect(takeFor(accountA)).toBeUndefined()
  })

  it('keeps the text only in sessionStorage, never in localStorage', () => {
    keep(accountA, draft)

    expect(sessionStorage.length).toBeGreaterThan(0)
    expect(localStorage.length).toBe(0)
  })
})

describe('discardUnlessOwnedBy', () => {
  it('removes text kept by a different account, without ever exposing it', () => {
    keep(accountA, draft)

    discardUnlessOwnedBy(accountB)

    expect(takeFor(accountA)).toBeUndefined()
  })

  it('leaves text kept by the same account untouched', () => {
    keep(accountA, draft)

    discardUnlessOwnedBy(accountA)

    expect(takeFor(accountA)).toEqual(draft)
  })

  it('is a no-op when nothing was kept', () => {
    expect(() => discardUnlessOwnedBy(accountA)).not.toThrow()
    expect(takeFor(accountA)).toBeUndefined()
  })
})

describe('clearAll', () => {
  it('removes every kept draft, for sign-out', () => {
    keep(accountA, draft)

    clearAll()

    expect(takeFor(accountA)).toBeUndefined()
    expect(sessionStorage.length).toBe(0)
  })
})
