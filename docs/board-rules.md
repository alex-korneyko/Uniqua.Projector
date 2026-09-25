# Board rules

Four rules govern a board, fixed in [spec §1](features/boards-columns-cards/spec.md): who can learn
that a board exists, which columns and cards a request may name, what keeps a board's structure
whole, and what happens to a change made against an outdated view. Each is written below with the
file that enforces it, the test that proves it, and a way to check it yourself from outside the
application.

If anything below disagrees with the code, **the code is right and this document is a defect** —
and [`BoardRulesDocumentTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardRulesDocumentTests.cs)
fails if a file named here stops existing.

For the reasoning behind each decision, follow the ADR link. This page stays short on purpose.

---

## 1. A non-member learns nothing

An account that is not a member of a board receives **one identical refusal** — `404`
`boards.not_available` — for any request that names it, whatever else is wrong with the request: an
invalid body, an outdated version, an identifier that is not a UUID, or a column or card from
another board. That refusal is the same, status, every header and every body member except
`instance` and `traceId`, as the one given for a board that never existed, and it never carries
board content — no name, no `current_card`, no `current_column`.

The board is loaded **already scoped to the caller's membership**, in one query. A board the caller
is not a member of and a board that does not exist both come back as nothing, so no later step can
tell them apart. The body is bound leniently: a body that is not JSON at all is refused before any
board is looked at — identically for every board — and every other judgement of its shape waits
until the member-scoped load has admitted the caller.

A visitor with no session is refused before any board is looked at, with the same `401` for a real
board and one that never existed.

- **Enforced by** [`src/Uniqua.Projector.Infrastructure/Boards/BoardStore.cs`](../src/Uniqua.Projector.Infrastructure/Boards/BoardStore.cs)
  (`LoadForMemberAsync`) and [`src/Uniqua.Projector.Api/Boards/BoardRequestBody.cs`](../src/Uniqua.Projector.Api/Boards/BoardRequestBody.cs)
  (lenient binding)
- **Proved by** [`tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardBoundaryTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardBoundaryTests.cs)
  — every board-naming operation, with valid, invalid, stale, non-UUID and cross-board inputs,
  compared against a never-existed board, and the board checked unchanged after each — and
  [`tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardStoreTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardStoreTests.cs)
  (`LoadForMemberAsync_returns_null_when_the_caller_is_not_a_member`)
- **Check it yourself:** sign in as an account that is not on a board, and send
  `PATCH /api/v1/boards/{id}` with an empty name, first to that board's id and then to a freshly
  made-up UUID. Both answers are `404` with the same body, apart from `instance` and `traceId`.
- **Why:** [ADR 0013](features/boards-columns-cards/adr/0013-grant-board-access-through-one-level-membership-records-with-an-owner-role.md)
  and [ADR 0014](features/boards-columns-cards/adr/0014-enforce-membership-by-loading-the-board-scoped-to-the-caller.md).

A member who is not the board owner can change every column and card exactly as the owner can, and
is refused only renaming and deleting the board itself — with `403` `boards.owner_only`, which is
answered only to a member, never to a non-member. `Board.EnsureOwner` in
[`src/Uniqua.Projector.Domain/Boards/Board.cs`](../src/Uniqua.Projector.Domain/Boards/Board.cs) is
that check, and `BoardBoundaryTests.cs` proves both halves.

## 2. Every column and card named belongs to the board that was checked

A request names a board, and may also name a column or a card. The column or card must belong to
**that** board — the one whose membership was just checked. One from any other board is refused
exactly as if it did not exist (`404` `boards.not_available`), even when the caller is a member of
both boards, and nothing changes on either.

A column is reachable only through its board: `Board.FindColumn` is the one way in from outside the
aggregate, so a use case has no means of reaching a column the board does not hold. A card is loaded
filtered on the board it must belong to, and `Board.RemoveCard` refuses a card whose board is
another.

- **Enforced by** [`src/Uniqua.Projector.Domain/Boards/Board.cs`](../src/Uniqua.Projector.Domain/Boards/Board.cs)
  (`FindColumn`, `RemoveCard`) and, for loading a card, [`src/Uniqua.Projector.Infrastructure/Boards/BoardStore.cs`](../src/Uniqua.Projector.Infrastructure/Boards/BoardStore.cs)
  (`FindCardAsync`)
- **Proved by** [`tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardBoundaryTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardBoundaryTests.cs)
  (`A_member_of_two_boards_naming_the_others_column_through_the_first_gets_not_available`,
  `A_member_of_two_boards_naming_the_others_card_through_the_first_gets_not_available`),
  [`tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnEndpointTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnEndpointTests.cs)
  and [`tests/Uniqua.Projector.Api.IntegrationTests/Boards/CardEndpointTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Boards/CardEndpointTests.cs)
  (`A_column_id_from_another_board_answers_not_available` in each), and
  [`tests/Uniqua.Projector.Domain.Tests/Boards/CardTests.cs`](../tests/Uniqua.Projector.Domain.Tests/Boards/CardTests.cs)
  (`A_card_whose_board_id_is_another_board_is_refused_as_not_available_on_removal`)
- **Check it yourself:** as a member of two boards, send
  `PATCH /api/v1/boards/{first}/columns/{a column of the second}`. The answer is `404`
  `boards.not_available`, and both boards read back unchanged.
- **Why:** [ADR 0014](features/boards-columns-cards/adr/0014-enforce-membership-by-loading-the-board-scoped-to-the-caller.md).

## 3. A column that holds cards cannot be deleted, and a board keeps at least one column

Deleting a column that still holds a card is refused with `409` `boards.column_not_empty`; deleting
the only column left is refused with `409` `boards.last_column`, even when it is empty. When both
apply, the not-empty refusal is the one given. This holds under simultaneous changes too: two members
deleting the last two columns at the same moment, or one adding a card while another deletes its
column, cannot between them break either rule, because every change to a board's structure moves a
concurrency token and a lost race is retried against the fresh board, at most three times.

- **Enforced by** [`src/Uniqua.Projector.Domain/Boards/Board.cs`](../src/Uniqua.Projector.Domain/Boards/Board.cs)
  (`DeleteColumn`) and, for the race, [`src/Uniqua.Projector.Application/Boards/BoardChangeRetry.cs`](../src/Uniqua.Projector.Application/Boards/BoardChangeRetry.cs)
- **Proved by** [`tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnEndpointTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnEndpointTests.cs)
  (`Deleting_a_column_that_still_holds_a_card_is_refused`, `Deleting_the_last_remaining_column_is_refused`),
  [`tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardInvariantRaceTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardInvariantRaceTests.cs)
  (1,000 randomised simultaneous pairs: 0 boards left with no column, 0 non-empty columns deleted)
  and [`tests/Uniqua.Projector.Domain.Tests/Boards/BoardColumnTests.cs`](../tests/Uniqua.Projector.Domain.Tests/Boards/BoardColumnTests.cs)
- **Check it yourself:** on a board with one card in its first column, send
  `DELETE /api/v1/boards/{id}/columns/{that column}` with its `name_version`: `409`
  `boards.column_not_empty`. On a fresh board with no cards, delete two of its three columns, then
  try the third: `409` `boards.last_column`.
- **Why:** [ADR 0015](features/boards-columns-cards/adr/0015-guard-board-invariants-with-an-optimistic-concurrency-token-and-bounded-retry.md)
  and [ADR 0017](features/boards-columns-cards/adr/0017-keep-column-positions-dense-and-card-positions-gapped.md).

## 4. A change against an outdated view is refused, not silently applied

Every change that could overwrite someone else's carries the version it was made against, and is
refused if that version has moved on — never applied over the newer change. The version is scoped to
the one thing it belongs to: a card's own `content_version` (`409` `boards.card_changed`, carrying
the current card), a column's own `name_version` (`409` `boards.column_renamed`, carrying the current
column, for renaming or deleting it), or the board's `column_layout_version` for moving a column
(`409` `boards.columns_changed`, carrying the current layout). Adding a column or a card overwrites
nothing, so it is never stale. A change to a *different* card or column is
therefore still accepted without reopening the board.

The current state travels only with a refusal given to a member. A non-member's stale change gets the
refusal of rule 1, with no current card or column in it.

- **Enforced by** [`src/Uniqua.Projector.Domain/Boards/Card.cs`](../src/Uniqua.Projector.Domain/Boards/Card.cs)
  (`Edit`, `EnsureDeletable`) and [`src/Uniqua.Projector.Domain/Boards/Board.cs`](../src/Uniqua.Projector.Domain/Boards/Board.cs)
  (`RenameColumn`, `MoveColumn`, `DeleteColumn`)
- **Proved by** [`tests/Uniqua.Projector.Api.IntegrationTests/Quality/StaleChangeScopeTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Quality/StaleChangeScopeTests.cs)
  (the version is scoped to what it belongs to),
  [`tests/Uniqua.Projector.Api.IntegrationTests/Boards/CardEndpointTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Boards/CardEndpointTests.cs)
  (`Editing_against_a_stale_content_version_is_refused_and_carries_the_current_card`),
  [`tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnEndpointTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnEndpointTests.cs)
  (`Renaming_against_a_stale_name_version_is_refused_and_carries_the_current_column`,
  `Moving_a_column_against_a_stale_layout_version_is_refused_and_carries_the_current_layout`)
  and [`tests/Uniqua.Projector.Domain.Tests/Boards/CardTests.cs`](../tests/Uniqua.Projector.Domain.Tests/Boards/CardTests.cs)
- **Check it yourself:** open a card in two browsers. Save an edit in one, then save a different edit
  in the other: the second is refused with `409` `boards.card_changed`, and the first edit is what
  the card still reads.
- **Why:** [ADR 0016](features/boards-columns-cards/adr/0016-detect-stale-changes-with-per-concern-version-counters-in-the-domain.md).

---

## The contract

The endpoints, their request and response shapes, and every refusal code are in
[`docs/features/boards-columns-cards/contracts/openapi.yaml`](features/boards-columns-cards/contracts/openapi.yaml),
which is the contract of record. Every refusal is named once, in
[`src/Uniqua.Projector.Domain/Boards/BoardError.cs`](../src/Uniqua.Projector.Domain/Boards/BoardError.cs),
and leaves the application as an RFC 9457 `application/problem+json` document from the one handler
in [`src/Uniqua.Projector.Api/ProblemDetailsSetup.cs`](../src/Uniqua.Projector.Api/ProblemDetailsSetup.cs).
