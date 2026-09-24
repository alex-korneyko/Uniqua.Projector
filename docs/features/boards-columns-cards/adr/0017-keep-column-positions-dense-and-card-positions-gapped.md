---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead"]
updated_at: "2026-09-24"
feature_size: "M"
ticket: "roadmap step 3 — boards-columns-cards"
---

# 0017 — Keep column positions dense and card positions gapped

- **Status:** Accepted
- **Date:** 2026-09-24
- **Deciders:** Alex Korneiko, with Claude during `/sdd:design boards-columns-cards`

## Context

Columns are added at the end, placed at a new position and deleted (AC-05, AC-06, AC-07); cards are only added at the end of their column and deleted in this feature (AC-12, AC-18), and moving a card is roadmap step 5 (spec §3). The storage format of positions is inherited by step 5, and changing it later means rewriting every stored position.

## Decision drivers

- Spec §6 NFR "Column order consistency" — 0 duplicated and 0 missing positions across 1,000 randomised sequences of accepted reorders, adds and deletes.
- AC-07 / AC-18 — the remaining columns, and the other cards of a column, keep their relative order after a deletion.
- Spec §6 NFR latency — p95 ≤ 200 ms for a single change.
- Content ceilings — at most 20 columns and 1,000 cards per board.

## Considered options

1. **Dense column positions, gapped card positions** — columns hold 0..n-1 and the Board renumbers them on every add, move or delete; cards hold ascending integers, a new card takes its column's next card position (kept on the column by the Board, one past the highest ever used), and a deletion leaves a gap.
2. **Fractional keys for both** — sortable string keys, so a move writes only the moved row.
3. **Dense integers for both** — columns and cards both hold 0..n-1, renumbered on every change.

## Decision outcome

**Chosen:** Option 1. With at most 20 columns, renumbering all of them inside the board's concurrency guard (ADR 0015) is cheap, and "positions are exactly 0..n-1" becomes one domain invariant the NFR test checks directly. Gapped card positions keep relative order on deletion without touching other rows. Option 2 is more mechanism than 20 columns and append-only cards need, and makes "no duplicates" depend on a key generator; option 3 would renumber up to about 1,000 cards on a single card deletion, inside the 200 ms budget, for nothing this feature needs.

## Consequences

**Positive**
- The column-order invariant is stated and checked in one place, the Board, and reads as 0, 1, 2 in the store.
- A card add or a card deletion touches one card row (plus the board row, per ADR 0015).

**Negative**
- Step 5 must still choose its card-reorder technique — renumber a column's cards on a move, or migrate cards to fractional keys; this record deliberately leaves that open with roadmap D2.
- A unique index on (board, column position) cannot hold row by row during a renumber; uniqueness is the domain's invariant plus the NFR test rather than a database constraint (the `data-model` stage confirms).

**Neutral**
- Card positions are unique within a column by construction: the column's next card position is read and advanced under the board guard, so adding a card never loads the column's cards.

## Links

- Spec: [[../spec.md]] AC-05, AC-06, AC-07, AC-12, AC-18, §3, §6
- SAD: [[../sad.md]] §4
- Related ADR: [[0015-guard-board-invariants-with-an-optimistic-concurrency-token-and-bounded-retry]]
