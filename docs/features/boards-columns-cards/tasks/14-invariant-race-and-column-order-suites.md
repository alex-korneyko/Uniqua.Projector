---
id: T14
title: "Prove the board invariants under 1,000 simultaneous pairs and the column order under 1,000 random sequences"
layer: "tests"
deps: ["T10", "T11"]
blocks: ["T13"]
acs: ["AC-03", "AC-10b", "AC-11", "AC-15", "AC-24b"]
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardInvariantRaceTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/ColumnOrderConsistencyTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/StaleChangeScopeTests.cs"
owner: "Alex Korneiko"
estimate: "L"
context_budget: "M"
status: "todo"
---

# T14 — Prove the board invariants under 1,000 simultaneous pairs and the column order under 1,000 random sequences

## Place in the sequence

- **Blocked by:** T10 — Expose the column endpoints: add, rename, move and delete, T11 — Expose the card endpoints: add, open, edit and delete · **Blocks:** T13 — Prove the membership boundary across every read and change kind, and map the four board rules to their tests · **Wave:** 7.
- **Lane:** own lane — runs beside T12 and T15.

## Why (user story)

> **As a** board member
> **I want** a change I make from an outdated view to be refused and explained rather than applied
> **So that** no one's work disappears without anyone noticing
>
> — `spec.md §4, US-08, verbatim` · full text: [spec.md](../spec.md)

This task proves that no board rule gives way under simultaneous changes, and that the stale check refuses only what it should.

## Inlined context

> | Board invariants under simultaneous changes | 0 boards left with no column, 0 non-empty columns deleted, 0 boards above 20 columns or 1,000 cards, and 0 accounts owning more than 50 boards, across 1,000 randomised pairs of simultaneous changes — column deletes, column adds, card adds and board creations, including pairs made one short of each ceiling | integration test issuing the pairs concurrently against the real store |
> | Column order consistency | 0 duplicated and 0 missing positions across 1,000 randomised sequences of accepted reorders, adds and deletes — every column of a board holds exactly one distinct position | integration test over randomised sequences |
>
> — `spec.md §6, NFR rows, verbatim` · full text: [spec.md](../spec.md)

> **How verify:** an integration test issuing the 1,000 pairs concurrently against the SQL Server container, with `ContentionForcer` — generalised from its current `AspNetUsers`-only match to board, column and card updates — guaranteeing that the one-short-of-the-ceiling pairs actually collide on the board row rather than happening to serialise; an integration test over 1,000 randomised column sequences that checks positions after every step; […] plus the AC-24b integration test.
>
> — `sad.md §10, QG-2, abridged` · full text: [sad.md](../sad.md)

> They insert directly, because 999 API calls would run into the 120-per-minute limit.
>
> — `data-model.md §Test fixtures, abridged` · full text: [data-model.md](../data-model.md)

**Fallback:** insufficient or contradicted by the code → read [sad.md](../sad.md) §10 and [spec.md](../spec.md) §6 in full and follow them. Do not guess.

## Data delta

No DB changes.

## API contract

Drives `createBoard`, `addColumn`, `deleteColumn`, `moveColumn`, `addCard`, `editCard`, `renameColumn` through HTTP. Accepted outcomes and the refusals `owned_board_limit_reached`, `column_limit_reached`, `card_limit_reached`, `column_not_empty`, `last_column`, `columns_changed`, `contended` are all legal results; only the invariants are asserted.

— `contracts/openapi.yaml, paths, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-03 — domain invariant

> **Given** an account that already owns 50 boards
> **When** they try to create another
> **Then** the system refuses and tells them an account can own at most 50 boards, because each account's share of the product is bounded — and the limit holds even when several boards are created at the same moment
>
> — `spec.md §5, AC-03, verbatim` · full text: [spec.md](../spec.md)

### AC-10b — domain invariant (concurrent)

> **Given** a board with exactly two columns, both empty, and two members each viewing it
> **When** each deletes a different one of the two columns at the same moment
> **Then** exactly one deletion succeeds, the other is refused because a board must keep at least one column, and the board is left with one column
>
> — `spec.md §5, AC-10b, verbatim` · full text: [spec.md](../spec.md)

### AC-11 — domain invariant

> **Given** a board member on a board that already has 20 columns
> **When** they try to add another
> **Then** the system refuses and tells them a board can hold at most 20 columns — and the limit holds even when several columns are added at the same moment
>
> — `spec.md §5, AC-11, verbatim` · full text: [spec.md](../spec.md)

### AC-15 — domain invariant

> **Given** a board member on a board that already holds 1,000 cards
> **When** they try to add another card to any of its columns
> **Then** the system refuses and tells them a board can hold at most 1,000 cards — and the limit holds even when several cards are added at the same moment
>
> — `spec.md §5, AC-15, verbatim` · full text: [spec.md](../spec.md)

### AC-24b — happy path

> **Given** two board members viewing the same board
> **When** one changes a card and, afterwards, the other changes a different card or a column, without having reopened the board
> **Then** the system accepts both changes, because a change is refused as stale only when the very thing it changes was changed since that member last saw it, as the stale-change rule below defines
>
> — `spec.md §5, AC-24b, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Race generator: seeded `Random`, 1,000 pairs drawn from {delete×delete, add-column×add-column at 19, add-card×add-card at 999, add-card×delete-column, create×create at 49}; each pair issued concurrently with `ContentionForcer` enabled for the one-short cases — `Quality/BoardInvariantRaceTests.cs`.
- [ ] After each pair, query the store directly: columns ≥ 1, ≤ 20; cards ≤ 1,000; no column deleted that held cards; owned boards ≤ 50.
- [ ] Column-order suite: 1,000 random sequences of add / move / delete through the API; after each accepted step, positions are exactly `{0..n-1}` — `Quality/ColumnOrderConsistencyTests.cs`.
- [ ] AC-24b: two members, one edits card X, the other then edits card Y and moves a column without reopening — both accepted — `Quality/StaleChangeScopeTests.cs`.
- [ ] Use distinct accounts per pair so the 120-per-minute limit never interferes; seed via `BoardFixtures`.
- [ ] Print the seed on failure so a failing run is reproducible.

## Edge cases

| Case | Behaviour |
|---|---|
| A pair that happens to serialise naturally | Still asserted; the forced cases guarantee the collisions exist |
| A change answered `boards.contended` after 3 conflicts | Legal outcome; invariants still hold; counted and reported |
| Seeded board at 19 columns, two adds | Exactly one lands |
| Run time | Bounded by using few boards per pair; the whole suite stays within the CI budget accepted for the container |

## Definition of Done

- [ ] All three suites pass with 0 violations across 1,000 pairs and 1,000 sequences.
- [ ] The forced-collision cases are shown to collide (conflict count > 0 recorded by the fixture).
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
