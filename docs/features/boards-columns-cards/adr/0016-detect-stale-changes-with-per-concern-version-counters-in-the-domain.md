---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead"]
updated_at: "2026-09-24"
feature_size: "M"
ticket: "roadmap step 3 — boards-columns-cards"
---

# 0016 — Detect stale changes with per-concern version counters in the domain

- **Status:** Accepted
- **Date:** 2026-09-24
- **Deciders:** Alex Korneiko, with Claude during `/sdd:design boards-columns-cards`

## Context

Spec §5 fixes a deliberately narrow stale-change rule: editing or deleting a card is stale only if that card's title or description changed; renaming or deleting a column only if that column was renamed; placing a column at a new position only if a column of the board was added, placed elsewhere or deleted; adding a column or a card is never stale (AC-06b, AC-23, AC-24, AC-24b). The refusal shows the current state and keeps what the member typed. It is the product's first concurrency rule and is expected to set the precedent for roadmap D2, the card move (spec §8, first open question).

## Decision drivers

- Quality goal 2 (SAD §1) — no silent loss: a change made from an outdated view is refused and explained.
- AC-24b — a change is refused only when *the very thing it changes* changed; any coarser token refuses legitimate changes.
- ADR 0014 — the refusal returns current state, so it is produced only after the membership check.
- `CLAUDE.md` — the rule is a domain invariant, enforced by the entity.

## Considered options

1. **Per-concern version counters in the domain** — `Card.ContentVersion` (title or description changed), `Column.NameVersion` (renamed), `Board.ColumnLayoutVersion` (a column added, moved or deleted); the client echoes the one it saw; the entity refuses a mismatch.
2. **The database row version as the token** — each row's `rowversion` is the version the client saw.
3. **Last-modified timestamps** — `ContentChangedAt`, `NameChangedAt`, `LayoutChangedAt`, compared to the value the client saw.

## Decision outcome

**Chosen:** Option 1. Each counter moves on exactly the events the spec names and on nothing else, so the rule reads in one place in Domain and cannot drift from AC-24b. Option 2 is too coarse: a column's row changes when its position changes, and the board's row changes on every card add (ADR 0015), so a reorder would make a pending rename stale and a card add would make a pending column reorder stale — both contradicting the spec. Option 3 makes clock precision part of a correctness rule: two changes at the same instant, routine under the controllable test clock, compare equal and a stale change slips through.

## Consequences

**Positive**
- The stale-change rule is three increments and three comparisons in Domain, each unit-tested at its boundary.
- The refusal carries the current value of just the thing that changed — the card, the column name, or the column order — which is what `ux-flows.md` says each refusal refreshes.
- Step 5 can extend the pattern to card position without reinterpreting the existing counters.

**Negative**
- Three counters to keep correct, and the client must carry each one it last saw and send it back.
- `ContentVersion` and `NameVersion` are also EF Core concurrency tokens on their rows: a save or delete is conditional on the counter still being the value the member saw, so two simultaneous edits cannot both land even after both passed the in-memory comparison (critic finding, 2026-09-24).
- Two kinds of version live side by side — ADR 0015's internal `rowversion` (race control, never shown to the client) and these counters (user-visible staleness); naming must keep them apart.

**Neutral**
- How a counter travels on the wire (a body field or an `If-Match` header) is the `api` stage's decision.

## Links

- Spec: [[../spec.md]] AC-06b, AC-23, AC-24, AC-24b, stale-change rule, §8 (first open question)
- SAD: [[../sad.md]] §4
- Related ADR: [[0015-guard-board-invariants-with-an-optimistic-concurrency-token-and-bounded-retry]], [[0014-enforce-membership-by-loading-the-board-scoped-to-the-caller]]
