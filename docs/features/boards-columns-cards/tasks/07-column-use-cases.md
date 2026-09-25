---
id: T7
title: "Write the column use cases: add, rename, move and delete, with races re-decided"
layer: "app"
deps: ["T2", "T5"]
blocks: ["T10"]
acs: ["AC-05", "AC-06", "AC-06b", "AC-07", "AC-09", "AC-10b", "AC-11", "AC-18b", "AC-21"]
files_hint:
  - "src/Uniqua.Projector.Application/Boards/AddColumn.cs"
  - "src/Uniqua.Projector.Application/Boards/RenameColumn.cs"
  - "src/Uniqua.Projector.Application/Boards/MoveColumn.cs"
  - "src/Uniqua.Projector.Application/Boards/DeleteColumn.cs"
  - "src/Uniqua.Projector.Application/DependencyInjection.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnUseCaseTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T7 — Write the column use cases: add, rename, move and delete, with races re-decided

## Place in the sequence

- **Blocked by:** T2 — Give the Board its column rules: add, rename, move and delete with dense positions and version checks, T5 — Declare the board ports and implement the member-scoped store with the bounded concurrency retry · **Blocks:** T10 — Expose the column endpoints: add, rename, move and delete · **Wave:** 4.
- **Lane:** shares `Application/DependencyInjection.cs` with T6 and T8 — serialized with them.

## Why (user story)

> **As a** board member
> **I want** to add, rename, reorder and delete the columns of a board
> **So that** the board reflects how the work actually flows
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

> **As a** board member who is not the board owner
> **I want** to change columns and cards exactly as the owner can, without being able to rename or delete the board
> **So that** I can collaborate fully without being able to take the board away from its owner
>
> — `spec.md §4, US-07, verbatim` · full text: [spec.md](../spec.md)

This task orchestrates the column actions against the store, so that a lost race is re-decided by the Board rather than let through.

## Inlined context

> `App->>Infra: Save A's deletion only if the board is still at T` → `Db-->>Infra: No row updated - the token moved` → `Infra-->>App: Concurrency conflict for B` → `App->>Infra: Reload the board for B` → `App->>Domain: B - may this column go` → `Domain-->>App: No, a board must keep at least one column`.
>
> — `sad.md §6, flow 3, abridged` · full text: [sad.md](../sad.md)

> Rename accepted: `App->>Infra: Save the column only if it is still at name version K` — persists the column name and its name version - no board-row update — `Written, or no row when another rename landed first - then the stale branch answers`. Add / move / delete: save the column rows and the board only if the board is still at its token; a conflict → reload and re-decide as in flow 3.
>
> — `sad.md §6, flows 6, 7, 8, abridged` · full text: [sad.md](../sad.md)

> No role is checked here - an owner and any other member are treated alike (AC-21).
>
> — `sad.md §6, flow 6 precondition, abridged` · full text: [sad.md](../sad.md)

> A concurrency exception on a column row during a *structural* change is therefore a retry under ADR 0015, not a stale refusal. Only a rename or delete of the column itself answers "renamed since you last saw it".
>
> — `data-model.md §Notes for implement, verbatim` · full text: [data-model.md](../data-model.md)

> **Hard rule:** A use case orchestrates; it does not decide what is legal.
>
> — `CLAUDE.md §Domain rules live in Domain, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([sad.md](../sad.md) · [data-model.md](../data-model.md) · [adr/0015](../adr/0015-guard-board-invariants-with-an-optimistic-concurrency-token-and-bounded-retry.md)) and follow it. Do not guess.

## Data delta

No DB changes — writes `Columns` rows and `Boards.ColumnLayoutVersion` / `RowVersion` (add, move, delete); `Columns.Name` / `NameVersion` only (rename).

## API contract

Internal — no API surface; T10 exposes these as `addColumn`, `renameColumn`, `moveColumn`, `deleteColumn`. Results carry `Column` (`id`, `name`, `position`, `name_version`) and, for add / move / delete, the board's `column_layout_version` (and for move / delete the full `ColumnLayout`).

— `contracts/openapi.yaml, components.schemas.Column + ColumnLayout + AddedColumn, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-05 — happy path

> **Given** a board member on a board with fewer than 20 columns
> **When** they add a column with a name of 1 to 50 characters
> **Then** the system adds the column at the end of the board and shows it to them; a column name need not be unique on its board
>
> — `spec.md §5, AC-05, verbatim` · full text: [spec.md](../spec.md)

### AC-06 — happy path

> **Given** a board member on a board with several columns
> **When** they rename a column, or place a column at a different position among the others
> **Then** the system records the new name or order, and every member who next opens the board sees the columns named and ordered that way
>
> — `spec.md §5, AC-06, verbatim` · full text: [spec.md](../spec.md)

### AC-06b — domain invariant

> **Given** a board member who is viewing a column, and that same column since renamed — by another member, or by the same account in another tab or on another device
> **When** the first member saves their own new name for it, or deletes it
> **Then** the system refuses it, tells them the column was renamed since they last saw it, shows its current name, and keeps any name they typed so they can apply it again
>
> — `spec.md §5, AC-06b, verbatim` · full text: [spec.md](../spec.md)

### AC-07 — happy path

> **Given** a board member on a board with at least two columns, one of which holds no cards
> **When** they delete that empty column
> **Then** the system removes it and the remaining columns keep their relative order
>
> — `spec.md §5, AC-07, verbatim` · full text: [spec.md](../spec.md)

### AC-09 — domain invariant

> **Given** a board member on a board where a column still holds at least one card
> **When** they try to delete that column
> **Then** the system refuses, leaves the column and its cards untouched, and tells them a column that still holds cards cannot be deleted
>
> — `spec.md §5, AC-09, verbatim` · full text: [spec.md](../spec.md)

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

### AC-18b — edge case

> **Given** a board member whose view still shows a column or card that has since been deleted
> **When** they submit a change that names it — editing or deleting that card, renaming or deleting that column, or adding a card to that column
> **Then** the system refuses it exactly as it refuses a column or card that never existed, changes nothing on the board, and keeps what they typed in place
>
> — `spec.md §5, AC-18b, verbatim` · full text: [spec.md](../spec.md)

### AC-21 — happy path

> **Given** a board member who is not its board owner
> **When** they add, rename, reorder or delete columns, or add, edit or delete cards
> **Then** the system accepts each change exactly as it would from the board owner, under the same rules
>
> — `spec.md §5, AC-21, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `AddColumn`, `MoveColumn`, `DeleteColumn` — load → Board method → save, wrapped in `BoardChangeRetry` so a board-token conflict reloads and re-asks — `Application/Boards/{AddColumn,MoveColumn,DeleteColumn}.cs`.
- [ ] `RenameColumn` — load → `Board.RenameColumn` → save conditional on `NameVersion`; a lost write reloads and answers the ordinary `ColumnRenamed` with the current column — `RenameColumn.cs`.
- [ ] No role check anywhere in these four (AC-21).
- [ ] Register in `AddApplication` — `src/Uniqua.Projector.Application/DependencyInjection.cs`.
- [ ] Integration tests with a `Member` record inserted directly through `AppDbContext` and the generalised `ContentionForcer` — `tests/.../Boards/ColumnUseCaseTests.cs`.

## Edge cases

| Case | Behaviour |
|---|---|
| Two members delete the two empty columns at once (forced collision) | One `Deleted`, one `LastColumn`; one column left (AC-10b) |
| A card is added to a column while it is being deleted (forced collision) | Delete re-decided: `ColumnNotEmpty` |
| Two renames of one column at once | One lands; the other `ColumnRenamed` with the winner's name |
| A column id from another board the caller belongs to | `NotAvailable`, nothing changed on either board |
| A deleted column's id | `NotAvailable` (AC-18b) |

## Definition of Done

- [ ] Use-case integration tests pass for each AC above and each edge-case row, with at least one run as a non-owner `Member`.
- [ ] Every one of the four begins with `LoadForMemberAsync`.
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
