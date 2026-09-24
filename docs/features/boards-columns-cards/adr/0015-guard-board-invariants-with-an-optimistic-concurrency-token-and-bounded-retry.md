---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead"]
updated_at: "2026-09-24"
feature_size: "M"
ticket: "roadmap step 3 — boards-columns-cards"
---

# 0015 — Guard board invariants with an optimistic concurrency token and bounded retry

- **Status:** Accepted
- **Date:** 2026-09-24
- **Deciders:** Alex Korneiko, with Claude during `/sdd:design boards-columns-cards`

## Context

Several board rules are counts or cross-row facts: at most 20 columns and 1,000 cards per board (AC-11, AC-15), at least one column (AC-10, AC-10b), no deleting a column that holds cards (AC-09), at most 50 owned boards per account (AC-03) — each "even when several changes arrive at the same moment". A read-check-write in the application lets two simultaneous writers both pass the check. This record decides how the rules stay true under races; it is separate from ADR 0016, which decides how a *member's* outdated view is detected.

## Decision drivers

- Spec §6 NFR "Board invariants under simultaneous changes" — 0 violations across 1,000 randomised simultaneous pairs, including pairs one short of each ceiling, against the real store.
- Spec §6 NFR latency — p95 ≤ 200 ms for a single change; throughput ≥ 50 changes/s across boards.
- `CLAUDE.md` — domain rules live in Domain; persistence is EF Core behind ports.
- The existing `ContentionForcer` test fixture forces a collision deterministically; it matches only the `AspNetUsers` failed-attempt update today and is generalised to board-row updates for this feature.

## Considered options

1. **Optimistic concurrency token with bounded retry** — the board row carries a `rowversion`; every structural change also updates the board row; a conflicting writer reloads, re-runs the domain rules and retries up to 3 times. The 50-board cap uses the same pattern on a per-account owned-board counter.
2. **Pessimistic row lock** — each structural change first locks the board row with an `UPDLOCK` hint inside a transaction.
3. **Serializable transactions** — each change runs at `SERIALIZABLE` isolation and the database's range locks keep counts true.

## Decision outcome

**Chosen:** Option 1. It is native to EF Core, keeps every rule in the entity (a retry simply asks the entity again against fresh state), and the collision can be forced in tests with a fixture that already exists. Option 2 needs raw SQL in Infrastructure and lets one slow request stall a whole board; option 3 hides the guarantee in isolation-level behaviour a reviewer cannot find in the code, and turns simultaneous inserts into deadlocks that need a retry loop anyway.

## Consequences

**Positive**
- Every board rule is decided by the Board aggregate against a consistent snapshot; a lost race is re-decided, never silently let through.
- Card adds, card deletes, and column adds, deletes and moves on one board are serialised by the one token; changes on different boards never contend.

**Negative**
- The board row is updated on every structural change (its card count at least), so it is a per-board hot row — fine under the per-account limit of 120 changes per minute, and a ceiling to re-measure if boards ever get many simultaneous members.
- A retry costs one extra round trip inside the 200 ms budget; after 3 conflicts the change fails with a retryable problem rather than looping.
- Board creation needs an owned-board counter per account that the domain owns; the account's Identity row is not the place (its `ConcurrencyStamp` belongs to Identity). The `data-model` stage places it.

**Neutral**
- Card title and description edits and column renames do not touch the board row; they race only with each other and with the item's deletion, and are settled by the item's own counter used as the write condition on its row (`ContentVersion`, `NameVersion` — ADR 0016).

## Links

- Spec: [[../spec.md]] AC-03, AC-09, AC-10, AC-10b, AC-11, AC-15, §6
- SAD: [[../sad.md]] §4
- Related ADR: [[0016-detect-stale-changes-with-per-concern-version-counters-in-the-domain]], [[0017-keep-column-positions-dense-and-card-positions-gapped]]
