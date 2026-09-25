---
id: T21
title: "Let members add, rename, drag and delete columns on the board, with every refusal shown in place"
layer: "ui"
deps: ["T20"]
blocks: []
acs: ["AC-05", "AC-06", "AC-06b", "AC-07", "AC-08", "AC-09", "AC-10", "AC-11", "AC-24"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/boards/ColumnRow.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/BoardColumn.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/ColumnHeader.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/AddColumnForm.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/ColumnManagement.test.tsx"
owner: "Alex Korneiko"
estimate: "L"
context_budget: "M"
status: "todo"
---

# T21 — Let members add, rename, drag and delete columns on the board, with every refusal shown in place

## Place in the sequence

- **Blocked by:** T20 — Build the board screen (SCR-04) with its owner header, card tiles and add-card form, and the not-available screen (SCR-08) · **Blocks:** nothing — it is a leaf · **Wave:** 3 — runs beside T22.
- **Lane:** own lane — it edits only the column files T20 created; T22 edits `BoardScreen.tsx` and its dialogs.

## Why (user story)

> **As a** board member
> **I want** to add, rename, reorder and delete the columns of a board
> **So that** the board reflects how the work actually flows
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task lets members shape the columns, and shows each column rule's refusal exactly where the member acted.

## Inlined context

> **Add column** — editing: `InlineNameEditor` (empty), hint «Between 1 and 50 characters.», «Add» / «Cancel» · pending «Adding…» · validation 400 `boards.column_name_invalid` — refusal line, typed name kept · limit 409 `boards.column_limit_reached` — refusal line with the ceiling; the control stays, and the server is the judge · success 201 — new `BoardColumn` at the end.
>
> **Column header** — default: drag handle `Button` (ghost, sm, `GripVertical`, aria-label «Move column <name>»), the name as `PlainText`, `Pencil` «Rename column», `Trash2` «Delete column».
> - rename stale — 409 `boards.column_renamed` + `current_column`: header patched; the editor stays open with the typed name; refusal line «The column was renamed. This column was renamed since you last saw it.» with the current name; «Save» applies the typed name against the new version.
> - moving — dnd-kit `SortableContext` on a horizontal list; pointer, or Space/Enter on the handle (keyboard sensor). move pending — new order shown optimistically, further drags paused. move stale — 409 `boards.columns_changed` + `current_layout`: cache patched, notice region «The columns changed since you last saw them.» move success — `column_layout_version` updated from the answer.
> - delete pending — no confirmation, because only an empty column can go. delete stale 409 `column_renamed` — header patched, refusal line, nothing deleted. delete not-empty 409 `column_not_empty` — «A column that still holds cards cannot be deleted.»; the button is **not** disabled beforehand. delete last-column 409 `last_column` — «A board must keep at least one column.» delete success — column removed, others keep their relative order.
> - rename / move / delete gone — 404, re-read, board available → board refreshed (rename: kept-text gone with the typed name).
>
> — `screens.md §SCR-04, Add column + Column header, abridged` · full text: [screens.md](../screens.md)

> `@dnd-kit/core` + `@dnd-kit/sortable` (column drag, keyboard sensor included) — dnd-kit is restricted to the board screen by `architecture-map.md` §Frontend.
>
> — `screens.md §Source, component inventory, abridged` · full text: [screens.md](../screens.md)

> **Hard rule:** Tailwind utility classes are the only styling mechanism […]. TanStack Query owns all server state.
>
> — `CLAUDE.md §Styling: Tailwind, once, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read [screens.md](../screens.md) SCR-04 in full and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

`addColumn {name}` → `AddedColumn`; `renameColumn {name, name_version}` → `Column` / 409 `column_renamed` + `current_column`; `moveColumn {position, column_layout_version}` → `ColumnLayout` / 409 `columns_changed` + `current_layout`; `deleteColumn {name_version}` → `ColumnLayout` / 409 `column_renamed` | `column_not_empty` | `last_column`. `busy` (503) is not declared on `renameColumn`.

— `contracts/openapi.yaml, column operations, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

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

## Checklist

- [ ] `AddColumnForm` at the end of the row, reusing `InlineNameEditor` — `AddColumnForm.tsx`, `ColumnRow.tsx`.
- [ ] `ColumnHeader` — handle, name, rename (`InlineNameEditor`), delete; refusal line under the header — `ColumnHeader.tsx`, used by `BoardColumn.tsx`.
- [ ] dnd-kit `DndContext` + horizontal `SortableContext` with pointer and keyboard sensors in `ColumnRow`; optimistic reorder, roll back to `current_layout` on stale.
- [ ] All four changes through `useBoardChange` (T17), sending the `name_version` / `column_layout_version` last seen.
- [ ] Component tests — `features/boards/__tests__/ColumnManagement.test.tsx`.

## Edge cases

| Case | Behaviour |
|---|---|
| Drag dropped at its own index | No request |
| Keyboard move (Space, arrows, Space) | Same request as a pointer drop |
| Delete a column with cards | Request is sent; server's `column_not_empty` shown |
| Stale rename, then Save again | Sent with the patched `name_version`; accepted |
| A second drag while one is pending | Paused until the answer |

## Definition of Done

- [ ] Component tests pass for every state above and every edge-case row.
- [ ] Column names render only through `PlainText`, including inside the aria-label.
- [ ] every Hard Rule inlined above still holds
- [ ] `npm --prefix src/Uniqua.Projector.Web run build` and `run lint` clean
