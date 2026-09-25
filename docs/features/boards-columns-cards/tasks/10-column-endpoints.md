---
id: T10
title: "Expose the column endpoints: add, rename, move and delete"
layer: "ports"
deps: ["T7", "T9"]
blocks: ["T13", "T14", "T15"]
acs: ["AC-05", "AC-06b", "AC-08", "AC-09", "AC-10", "AC-11", "AC-21", "AC-24"]
files_hint:
  - "src/Uniqua.Projector.Api/Boards/BoardEndpoints.Columns.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardEndpoints.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardProblems.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Fixtures/BoardFixtures.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T10 — Expose the column endpoints: add, rename, move and delete

## Place in the sequence

- **Blocked by:** T7 — Write the column use cases: add, rename, move and delete, with races re-decided, T9 — Expose the board endpoints with lenient binding and map every board refusal to one problem document · **Blocks:** T13 — Prove the membership boundary across every read and change kind, and map the four board rules to their tests, T14 — Prove the board invariants under 1,000 simultaneous pairs and the column order under 1,000 random sequences, T15 — Measure opening a full board, a single change and change throughput against the §6 budgets · **Wave:** 6.
- **Lane:** shares `BoardEndpoints.cs`, `BoardProblems.cs` and `BoardFixtures.cs` with T9, T11, T12 — serialized with them.

## Why (user story)

> **As a** board member
> **I want** to add, rename, reorder and delete the columns of a board
> **So that** the board reflects how the work actually flows
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task puts the column actions on the wire, with the version counter each one is checked against travelling in the body.

## Inlined context

> Every change that can be stale carries the version counter the member last saw (ADR 0016). A stale change is refused with the current state of just the thing that changed (AC-06b, AC-23, AC-24).
>
> — `contracts/openapi.yaml, info.description rule 4, verbatim` · full text: [openapi.yaml](../contracts/openapi.yaml)

> The version travels in the request body, like every other change (sad.md §8 Error handling; ADR 0014 lenient binding). No confirmation is asked, since only an empty column can go.
>
> — `contracts/openapi.yaml, operationId deleteColumn, verbatim` · full text: [openapi.yaml](../contracts/openapi.yaml)

> `ABoardWithColumnsAsync(…, int columnCount)` […] seed the board up to one short of each ceiling (19 columns, 999 cards) for AC-11, AC-15 and the spec §6 race pairs. They insert directly, because 999 API calls would run into the 120-per-minute limit, and they keep `Boards.CardCount`, `Columns.CardCount` and `NextCardPosition` consistent with the rows they insert.
>
> — `data-model.md §Test fixtures, abridged` · full text: [data-model.md](../data-model.md)

> **Hard rule:** An endpoint never builds an error shape of its own and never returns a bare status code with an ad-hoc body.
>
> — `CLAUDE.md §Errors are ProblemDetails, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read [openapi.yaml](../contracts/openapi.yaml) in full and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

| Operation | Body | Success | Refusals |
|---|---|---|---|
| `POST /api/v1/boards/{boardId}/columns` `addColumn` | `{name}` | 201 `AddedColumn {column, column_layout_version}` | 400 `column_name_invalid`; 409 `column_limit_reached`; 503 |
| `PATCH …/columns/{columnId}` `renameColumn` | `{name, name_version}` | 200 `Column` | 400 `column_name_invalid`; 409 `column_renamed` + `current_column` (no 503) |
| `PUT …/columns/{columnId}/position` `moveColumn` | `{position, column_layout_version}` | 200 `ColumnLayout` | 400 `column_position_invalid` (after the stale check); 409 `columns_changed` + `current_layout`; 503 |
| `DELETE …/columns/{columnId}` `deleteColumn` | `{name_version}` | 200 `ColumnLayout` | 409 `column_renamed` → `column_not_empty` → `last_column`, in that order; 503 |

All four: 400 `request_malformed` (before membership) / `request_invalid` (after); 404 `not_available` for an absent board, a non-member, or a column not on this board.

— `contracts/openapi.yaml, paths …/columns*, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-05 — happy path

> **Given** a board member on a board with fewer than 20 columns
> **When** they add a column with a name of 1 to 50 characters
> **Then** the system adds the column at the end of the board and shows it to them; a column name need not be unique on its board
>
> — `spec.md §5, AC-05, verbatim` · full text: [spec.md](../spec.md)

### AC-06b — domain invariant

> **Given** a board member who is viewing a column, and that same column since renamed — by another member, or by the same account in another tab or on another device
> **When** the first member saves their own new name for it, or deletes it
> **Then** the system refuses it, tells them the column was renamed since they last saw it, shows its current name, and keeps any name they typed so they can apply it again
>
> — `spec.md §5, AC-06b, verbatim` · full text: [spec.md](../spec.md)

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

### AC-21 — happy path

> **Given** a board member who is not its board owner
> **When** they add, rename, reorder or delete columns, or add, edit or delete cards
> **Then** the system accepts each change exactly as it would from the board owner, under the same rules
>
> — `spec.md §5, AC-21, verbatim` · full text: [spec.md](../spec.md)

### AC-24 — domain invariant

> **Given** a board member whose view of a board's columns is outdated because a column has since been added, reordered or deleted — by another member, or by the same account in another tab or on another device; a rename alone does not change the order
> **When** they place a column at a new position
> **Then** the system refuses the reorder, tells them the columns changed since they last saw them, and shows the current columns and order
>
> — `spec.md §5, AC-24, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `MapColumnEndpoints` on the board group — `Api/Boards/BoardEndpoints.Columns.cs`, called from `BoardEndpoints.cs`.
- [ ] Read `name`, `name_version`, `position`, `column_layout_version` through `BoardRequestBody` after the load; wrong type or absent → `request_invalid`.
- [ ] Map the new `BoardError` cases (with `current_column` / `current_layout` extension members) — `Api/Boards/BoardProblems.cs`.
- [ ] `ABoardWithColumnsAsync` — `tests/.../Fixtures/BoardFixtures.cs`.
- [ ] Endpoint tests, owner and `Member` — `tests/.../Boards/ColumnEndpointTests.cs`.

## Edge cases

| Case | Behaviour |
|---|---|
| Non-member sends a stale `name_version` | 404 `not_available` — no `current_column` ever reaches a non-member |
| `position: 7` on a 3-column board with a current layout version | 400 `column_position_invalid` |
| `position: 7` with a stale layout version | 409 `columns_changed` — stale first |
| `columnId` of another board | 404 `not_available` |
| `DELETE` with no body | 400 `request_invalid` after membership (a non-member still gets 404) |

## Definition of Done

- [ ] Endpoint integration tests pass for every AC above and every edge-case row, including one pass as a `Member`.
- [ ] Response bodies validate against the contract's `Column`, `ColumnLayout`, `AddedColumn` schemas (no extra members).
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
