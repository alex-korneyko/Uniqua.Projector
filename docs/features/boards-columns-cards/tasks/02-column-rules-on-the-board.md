---
id: T2
title: "Give the Board its column rules: add, rename, move and delete with dense positions and version checks"
layer: "domain"
deps: ["T1"]
blocks: ["T7"]
acs: ["AC-05", "AC-06", "AC-06b", "AC-07", "AC-08", "AC-09", "AC-10", "AC-11", "AC-24", "AC-24b"]
files_hint:
  - "src/Uniqua.Projector.Domain/Boards/Board.cs"
  - "src/Uniqua.Projector.Domain/Boards/Column.cs"
  - "src/Uniqua.Projector.Domain/Boards/BoardError.cs"
  - "tests/Uniqua.Projector.Domain.Tests/Boards/BoardColumnTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T2 — Give the Board its column rules: add, rename, move and delete with dense positions and version checks

## Place in the sequence

- **Blocked by:** T1 — Build the Board aggregate with the Text rule, board creation and the owner-only rules · **Blocks:** T7 — Write the column use cases: add, rename, move and delete, with races re-decided · **Wave:** 2 — runs beside T4 (schema) once T1's shapes exist.
- **Lane:** shares `Board.cs`, `Column.cs` and `BoardError.cs` with T1 and T3 — serialized with them.

## Why (user story)

> **As a** board member
> **I want** to add, rename, reorder and delete the columns of a board
> **So that** the board reflects how the work actually flows
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

> **As a** board member
> **I want** a change I make from an outdated view to be refused and explained rather than applied
> **So that** no one's work disappears without anyone noticing
>
> — `spec.md §4, US-08, verbatim` · full text: [spec.md](../spec.md)

This task puts every column rule inside the Board, so the use cases in T7 only ask and never decide.

## Inlined context

> **Stale-change rule — what each change is checked against.** A change is refused as stale when, since the member last saw it:
> - *editing or deleting a card* — that card's title or description was changed (a change to either counts, whichever the member is changing) (AC-23);
> - *renaming or deleting a column* — that column was renamed (AC-06b);
> - *placing a column at a new position* — a column of the board was added, placed elsewhere or deleted (AC-24);
> - *adding a column or a card* — never; it is placed at the end and overwrites nothing.
>
> Adding, editing or deleting a card is not a change to its column. The rule is the same whoever made the earlier change — another member, or the same account in another tab or on another device.
>
> — `spec.md §5, Stale-change rule, verbatim` · full text: [spec.md](../spec.md)

> **Chosen:** Option 1 [dense column positions, gapped card positions]. With at most 20 columns, renumbering all of them inside the board's concurrency guard (ADR 0015) is cheap, and "positions are exactly 0..n-1" becomes one domain invariant the NFR test checks directly.
>
> — `adr/0017, Decision outcome, abridged` · full text: [0017](../adr/0017-keep-column-positions-dense-and-card-positions-gapped.md)

> `Card.ContentVersion` (title or description changed), `Column.NameVersion` (renamed), `Board.ColumnLayoutVersion` (a column added, moved or deleted); the client echoes the one it saw; the entity refuses a mismatch. […] The refusal carries the current value of just the thing that changed — the card, the column name, or the column order.
>
> — `adr/0016, Considered options 1 + Consequences, abridged` · full text: [0016](../adr/0016-detect-stale-changes-with-per-concern-version-counters-in-the-domain.md)

> Delete: on this board → renamed since (stale) → still holds cards → the board's only column. It is accepted only for an empty column when another column remains. The remaining columns are renumbered 0..n-1 and keep their relative order, and the board's column layout version moves on. […] Move: `position` is the column's new zero-based place among the board's columns as they are once the move is done, so it must lie in 0..n-1. It is checked after the stale check.
>
> — `contracts/openapi.yaml, operationIds deleteColumn + moveColumn, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

> For a column deletion, stale comes before holds-cards and last-column.
>
> — `sad.md §6, Flagged for the stages that follow, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** An invariant — "a card cannot move to a column of another board", "a board must keep at least one column" — is enforced by the entity itself, never by a use case and never by an endpoint.
>
> — `CLAUDE.md §Domain rules live in Domain, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

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

### AC-08 — error

> **Given** a board member adding or renaming a column
> **When** they submit a name that is empty once surrounding spaces are removed, or longer than 50 characters
> **Then** the system refuses and tells them a column name must be between 1 and 50 characters, leaving what they typed in place
>
> — `spec.md §5, AC-08, verbatim` · full text: [spec.md](../spec.md)

### AC-09 — domain invariant

> **Given** a board member on a board where a column still holds at least one card
> **When** they try to delete that column
> **Then** the system refuses, leaves the column and its cards untouched, and tells them a column that still holds cards cannot be deleted
>
> — `spec.md §5, AC-09, verbatim` · full text: [spec.md](../spec.md)

### AC-10 — domain invariant

> **Given** a board member on a board that has exactly one column, holding no cards
> **When** they try to delete it
> **Then** the system refuses and tells them a board must keep at least one column
>
> — `spec.md §5, AC-10, verbatim` · full text: [spec.md](../spec.md)

### AC-11 — domain invariant

> **Given** a board member on a board that already has 20 columns
> **When** they try to add another
> **Then** the system refuses and tells them a board can hold at most 20 columns — and the limit holds even when several columns are added at the same moment
>
> — `spec.md §5, AC-11, verbatim` · full text: [spec.md](../spec.md)

### AC-24 — domain invariant

> **Given** a board member whose view of a board's columns is outdated because a column has since been added, reordered or deleted — by another member, or by the same account in another tab or on another device; a rename alone does not change the order
> **When** they place a column at a new position
> **Then** the system refuses the reorder, tells them the columns changed since they last saw them, and shows the current columns and order
>
> — `spec.md §5, AC-24, verbatim` · full text: [spec.md](../spec.md)

### AC-24b — happy path

> **Given** two board members viewing the same board
> **When** one changes a card and, afterwards, the other changes a different card or a column, without having reopened the board
> **Then** the system accepts both changes, because a change is refused as stale only when the very thing it changes was changed since that member last saw it, as the stale-change rule below defines
>
> — `spec.md §5, AC-24b, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `BoardError` additions: `ColumnNameInvalid`, `ColumnLimitReached`, `ColumnRenamed(currentColumn)`, `ColumnNotEmpty`, `LastColumn`, `ColumnsChanged(currentLayout)`, `ColumnPositionInvalid` — `Domain/Boards/BoardError.cs`.
- [ ] `Board.FindColumn(columnId)` — the only way a column is reached; a column not in `Columns` is `NotAvailable` (AC-26 by construction).
- [ ] `Board.AddColumn(name)` — Text rule 1–50, refuse at 20, append at `n`, `ColumnLayoutVersion++`, `NameVersion = 1`.
- [ ] `Board.RenameColumn(columnId, name, seenNameVersion)` — precedence: not on board → Text rule → stale (`NameVersion != seen`, carrying the column) → rename, `NameVersion++`, layout version unchanged.
- [ ] `Board.MoveColumn(columnId, position, seenLayoutVersion)` — not on board → stale (carrying the layout) → `position` in 0..n-1 → renumber 0..n-1, `ColumnLayoutVersion++`; `NameVersion` untouched.
- [ ] `Board.DeleteColumn(columnId, seenNameVersion)` — not on board → stale → `CardCount > 0` → last column → remove, renumber, `ColumnLayoutVersion++`.
- [ ] A private invariant check after every structural change: positions are exactly `{0..n-1}` and `1 ≤ n ≤ 20`.
- [ ] Unit tests — `tests/Uniqua.Projector.Domain.Tests/Boards/BoardColumnTests.cs`.

## Edge cases

| Case | Behaviour |
|---|---|
| Rename after only a card was added to the column | Accepted — a card change is not a change to its column |
| Move after another column was only renamed | Accepted — a rename does not move `ColumnLayoutVersion` (AC-24b) |
| Move to the column's own position | Accepted, positions unchanged, layout version still moves on |
| Move to position `n` or `-1` | `ColumnPositionInvalid`, checked only after the stale check |
| Delete a renamed column that also holds cards | `ColumnRenamed` — stale is checked first |
| Delete the only column while it holds cards | `ColumnNotEmpty` — holds-cards comes before last-column |
| 20th column added | Accepted; 21st refused `ColumnLimitReached` |
| Delete the middle of three columns | The other two become 0 and 1, same relative order (AC-07) |

## Definition of Done

- [ ] Domain unit tests pass for every row above and each AC's domain decision, including the precedence orders.
- [ ] A property-style unit test applying random adds/moves/deletes asserts positions stay exactly 0..n-1 after each step.
- [ ] Each stale error carries the current column or layout, never more.
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
