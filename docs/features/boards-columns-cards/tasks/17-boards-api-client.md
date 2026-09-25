---
id: T17
title: "Write the typed boards API client, its query keys and cache patches, and the board refusal wording"
layer: "ui"
deps: []
blocks: ["T19", "T20"]
acs: ["AC-18b"]
files_hint:
  - "src/Uniqua.Projector.Web/src/api/boards.ts"
  - "src/Uniqua.Projector.Web/src/api/__tests__/boards.test.ts"
  - "src/Uniqua.Projector.Web/src/features/boards/boardRefusals.ts"
  - "src/Uniqua.Projector.Web/src/features/boards/useBoardChange.ts"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/useBoardChange.test.tsx"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T17 — Write the typed boards API client, its query keys and cache patches, and the board refusal wording

## Place in the sequence

- **Blocked by:** nothing — it starts in wave 1 · **Blocks:** T19 — Build the board list (SCR-02) and the create-board dialog (SCR-03), T20 — Build the board screen (SCR-04) with its owner header, card tiles and add-card form, and the not-available screen (SCR-08) · **Wave:** 1 — built from the contract alone, in parallel with the backend.
- **Lane:** own lane.

## Why (user story)

> **As a** board member
> **I want** a change I make from an outdated view to be refused and explained rather than applied
> **So that** no one's work disappears without anyone noticing
>
> — `spec.md §4, US-08, verbatim` · full text: [spec.md](../spec.md)

This task is the client's one door to the boards API, and the one place a refusal becomes words and a stale answer becomes a patched cache.

## Inlined context

> **Refusal wording (applies to every refusal below).** The text shown is the server's problem `title` followed by its `detail`, the same way `describeRefusal` in `features/auth/accountRefusals.ts` builds it, so the wording lives in one place (`BoardProblems.cs`). […] The client writes its own text in only four cases:
> - «That column no longer exists.» and «That card no longer exists.» — used when a `boards.not_available` answer to a change turns out, after the board is read again, to concern a column or card rather than the board (sad.md §6 flow 2).
> - «We could not reach the server. Please check your connection and try again.» — used when there is no answer at all.
> - «Something went wrong. Please try again.» — used for any code with no row of its own […].
>
> Every rate-limit and busy message ends with «Try again in N seconds.», taken from `retry_after_seconds`, as `rateLimitMessage` does.
>
> — `screens.md §Source, Refusal wording, abridged` · full text: [screens.md](../screens.md)

> `Spa->>Api: Re-read the board to tell a gone card from a gone board` — Board still available - stay on it, refresh it, keep the typed text (AC-18b). Board not available - show the board-not-available screen.
>
> — `sad.md §6, flow 2, abridged` · full text: [sad.md](../sad.md)

> TanStack Query owns the board summary and each card's detail; accepted changes and stale refusals patch both from the server's answer. React Router's loaders and actions are not used.
>
> — `sad.md §8, Client state, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** TanStack Query owns all server state. A push event invalidates or patches the cached board rather than maintaining a second copy of it. No global client-state library is introduced until something actually needs one.
>
> — `CLAUDE.md §Styling: Tailwind, once (TanStack paragraph), verbatim` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read [openapi.yaml](../contracts/openapi.yaml) and `src/Uniqua.Projector.Web/src/api/accounts.ts` in full and follow them. Do not guess.

## Data delta

No DB changes.

## API contract

The 13 operations (method, path, body → success): `listMyBoards` GET `/api/v1/boards` → `BoardListPage`; `createBoard` POST `{name}` → 201 `Board`; `openBoard` GET `/{boardId}` → `Board`; `renameBoard` PATCH `{name}` → `BoardName`; `deleteBoard` DELETE `{confirm_name}` → 204; `addColumn` POST `/{boardId}/columns` `{name}` → `AddedColumn`; `renameColumn` PATCH `/columns/{columnId}` `{name, name_version}` → `Column`; `moveColumn` PUT `/columns/{columnId}/position` `{position, column_layout_version}` → `ColumnLayout`; `deleteColumn` DELETE `/columns/{columnId}` `{name_version}` → `ColumnLayout`; `addCard` POST `/{boardId}/cards` `{column_id, title, description?}` → `CardSummary`; `openCard` GET `/cards/{cardId}` → `Card`; `editCard` PATCH `{title?, description?, content_version}` → `Card`; `deleteCard` DELETE `{content_version}` → 204. Every change sends `X-XSRF-TOKEN`. Problem members read: `code`, `title`, `detail`, `retry_after_seconds`, `current_card`, `current_column`, `current_layout`, `current_name`.

— `contracts/openapi.yaml, paths + components.schemas, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-18b — edge case

> **Given** a board member whose view still shows a column or card that has since been deleted
> **When** they submit a change that names it — editing or deleting that card, renaming or deleting that column, or adding a card to that column
> **Then** the system refuses it exactly as it refuses a column or card that never existed, changes nothing on the board, and keeps what they typed in place
>
> — `spec.md §5, AC-18b, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Types for every schema and one function per operation over the existing `request<T>()` — `src/api/boards.ts`.
- [ ] Query keys `['boards']`, `['board', id]`, `['card', boardId, cardId]` and patch helpers: apply an accepted change or a `current_*` payload to the board and card caches.
- [ ] `boardRefusals.ts` — `describeBoardRefusal(error)` per the wording rules above (title + detail, the four client texts, the `retry_after_seconds` suffix), reusing `accountRefusals.ts` helpers rather than copying them.
- [ ] `useBoardChange` — wraps a mutation: on 404, re-read the board once and resolve to `item-gone` (board still there) or `board-gone`; on 401, resolve to `session-ended` (T18 keeps the text); never swallows typed text.
- [ ] Unit tests — `src/api/__tests__/boards.test.ts`, `features/boards/__tests__/useBoardChange.test.tsx`.

## Edge cases

| Case | Behaviour |
|---|---|
| 404 on `editCard`, re-read 200 | `item-gone` → «That card no longer exists.» |
| 404 on `addCard`, re-read 404 | `board-gone` → SCR-08 |
| 409 `boards.column_renamed` | Board cache's column patched from `current_column` |
| `fetch` rejects (offline) | «We could not reach the server. Please check your connection and try again.» |
| 400 `boards.request_invalid` | «Something went wrong. Please try again.» |

## Definition of Done

- [ ] Unit tests pass for every operation's request shape and every edge-case row.
- [ ] No second cache of board data outside TanStack Query.
- [ ] every Hard Rule inlined above still holds
- [ ] `npm --prefix src/Uniqua.Projector.Web run build` and `run lint` clean
