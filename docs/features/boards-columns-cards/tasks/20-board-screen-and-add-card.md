---
id: T20
title: "Build the board screen (SCR-04) with its owner header, card tiles and add-card form, and the not-available screen (SCR-08)"
layer: "ui"
deps: ["T16", "T17", "T18"]
blocks: ["T21", "T22"]
acs: ["AC-12", "AC-15", "AC-18b", "AC-19", "AC-22", "AC-25", "AC-28", "AC-17"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/boards/BoardScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/BoardHeader.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/ColumnRow.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/BoardColumn.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/AddCardForm.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/InlineNameEditor.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/BoardNotAvailable.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/BoardScreen.test.tsx"
owner: "Alex Korneiko"
estimate: "L"
context_budget: "M"
status: "todo"
---

# T20 — Build the board screen (SCR-04) with its owner header, card tiles and add-card form, and the not-available screen (SCR-08)

## Place in the sequence

- **Blocked by:** T16 — Vendor Dialog and Textarea, add the destructive Button variant, and build PlainText and KeptTextNotice, T17 — Write the typed boards API client, its query keys and cache patches, and the board refusal wording, T18 — Give the client board addresses with React Router, a guarded return address, and typed text kept across a sign-in · **Blocks:** T21 — Let members add, rename, drag and delete columns on the board, with every refusal shown in place, T22 — Build the card detail dialog with edit and delete (SCR-05, SCR-06) and the board deletion dialog (SCR-07) · **Wave:** 2 — runs beside T19.
- **Lane:** own lane. It creates `ColumnRow.tsx` and `BoardColumn.tsx`, which T21 extends, and `BoardScreen.tsx`, which T22 extends — so T21 and T22 can then run in parallel.

## Why (user story)

> **As a** board member
> **I want** to add a card with a title and an optional description to a column, and edit both later
> **So that** each piece of work is written down where everyone on the board can see it
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

> **As a** board owner
> **I want** any account that is not a member to be unable to see, change, or even confirm the existence of my board
> **So that** what I write on it is shared only with the people I chose
>
> — `spec.md §4, US-09, verbatim` · full text: [spec.md](../spec.md)

This task builds where the thin path lands: a board a member can read and add cards to, and one refusal screen that says nothing about why.

## Inlined context

> | State | Trigger / condition | Components |
> |---|---|---|
> | loading | `openBoard` in flight, with no seeded cache | status line «Loading the board…» |
> | error | 500 on `openBoard`, or no answer | failed block «We could not load this board» + «Try again» |
> | not-available | 404 `boards.not_available` on `openBoard` | SCR-08 at the same address |
> | session-ended (read) | 401 on `openBoard` | SCR-01 `from-board-link` with `returnTo` = this board |
> | default (member) | 200, `is_owner: false` | Header: `Button` (ghost, sm) «← My boards» and the board name as `PlainText` in an `h1`, with no rename or delete controls. Body: one `BoardColumn` per column in `position` order, then the add-column slot |
> | default (owner) | 200, `is_owner: true` | The same, plus a `Button` (ghost, sm) `Pencil` «Rename board» → `InlineNameEditor`, and a `Button` (outline, sm) «Delete board» → SCR-07 |
> | kept-text offer | Back on this board after SCR-01, same account, with kept text for it (AC-28) | `KeptTextNotice` in the notice region; «Apply again» resubmits it as an ordinary change |
> | kept-text gone | 404 on a change, board re-read still available (AC-18b) | Board refreshed; `KeptTextNotice` «That column no longer exists.» / «That card no longer exists.» with the typed text, «Dismiss». If the re-read answers 404 → SCR-08 |
>
> Board rename (owner): editing / pending «Saving…» / validation 400 `board_name_invalid` / owner-only 403 → refusal line, board re-read, controls disappear with `is_owner: false` / success 200 — title and cached list patched. Add card: closed «+ Add card» / editing `Input` «Title» (hint «Between 1 and 150 characters.») + `Textarea` «Description (optional)» (hint «At most 10,000 characters.») / pending / validation 400 title or description, under that field, both kept / limit 409 `card_limit_reached` / gone → kept-text gone / success 201 — title last in its column as a tile, form clears and stays open. **Card tile:** `Button` (outline, full width, `h-auto`, left-aligned, `whitespace-nowrap` overridden) wrapping the title as `PlainText`; pressing it opens SCR-05; ordered by `position`.
>
> — `screens.md §SCR-04, abridged` · full text: [screens.md](../screens.md)

> **SCR-08** `Card`: `CardTitle` «Board not available», `CardDescription` «This board does not exist, or you are not a member of it.», `Button` «Back to my boards» → SCR-02. No name, no id and no hint of which case applies. `document.title` carries no board name either.
>
> — `screens.md §SCR-08, abridged` · full text: [screens.md](../screens.md)

> Board, column and card text is rendered only as React text nodes — `dangerouslySetInnerHTML` is never used for it, nothing is turned into a link, and Tailwind `whitespace-pre-wrap` keeps line breaks and repeated spaces exactly as typed.
>
> — `sad.md §8, Text rendering, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** Tailwind utility classes are the only styling mechanism […]. TanStack Query owns all server state.
>
> — `CLAUDE.md §Styling: Tailwind, once, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read [screens.md](../screens.md) SCR-04/SCR-08 in full and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

`openBoard` → `Board {id, name, is_owner, column_layout_version, columns[], cards[]}`; `renameBoard {name}` → `BoardName`, refusals 400 `board_name_invalid`, 403 `owner_only`; `addCard {column_id, title, description?}` → 201 `CardSummary`, refusals 400 `card_title_invalid` / `card_description_invalid`, 409 `card_limit_reached`, 404 `not_available`; all changes also 429 / 503 / 401 — through T17's client and `useBoardChange`.

— `contracts/openapi.yaml, operationIds openBoard + renameBoard + addCard, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-12 — happy path

> **Given** a board member on a board holding fewer than 1,000 cards
> **When** they add a card to a column with a title of 1 to 150 characters and, optionally, a description of up to 10,000 characters
> **Then** the system adds the card at the end of that column and shows its title on the board
>
> — `spec.md §5, AC-12, verbatim` · full text: [spec.md](../spec.md)

### AC-15 — domain invariant

> **Given** a board member on a board that already holds 1,000 cards
> **When** they try to add another card to any of its columns
> **Then** the system refuses and tells them a board can hold at most 1,000 cards — and the limit holds even when several cards are added at the same moment
>
> — `spec.md §5, AC-15, verbatim` · full text: [spec.md](../spec.md)

### AC-18b — edge case

> **Given** a board member whose view still shows a column or card that has since been deleted
> **When** they submit a change that names it — editing or deleting that card, renaming or deleting that column, or adding a card to that column
> **Then** the system refuses it exactly as it refuses a column or card that never existed, changes nothing on the board, and keeps what they typed in place
>
> — `spec.md §5, AC-18b, verbatim` · full text: [spec.md](../spec.md)

### AC-19 — happy path

> **Given** the board owner viewing their board
> **When** they rename it to a name of 1 to 100 characters
> **Then** the system records the new name and every member sees it in their list of boards
>
> — `spec.md §5, AC-19, verbatim` · full text: [spec.md](../spec.md)

### AC-22 — authorization

> **Given** a board member who is not its board owner
> **When** they try to rename the board or delete it
> **Then** the system refuses, leaves the board unchanged, and tells them only the board owner may rename or delete a board
>
> — `spec.md §5, AC-22, verbatim` · full text: [spec.md](../spec.md)

### AC-25 — authorization

> **Given** an account that is not a member of a board
> **When** they try to open it, or submit any change to it or to anything on it — including a change that is itself invalid or based on an outdated view
> **Then** the system gives them exactly the same refusal it gives for a board that does not exist, reveals nothing of the board's content, and changes nothing on any board
>
> — `spec.md §5, AC-25, verbatim` · full text: [spec.md](../spec.md)

### AC-28 — cross-context

> **Given** a board member whose session has ended — they signed out on this browser, or it expired — while the board is still open in front of them
> **When** they submit a change
> **Then** the system changes nothing on the board, presents the sign-in form, and does not treat the change as coming from that member; what they typed is kept in this browser so that, once they sign in again as the same account, they can apply it again, and it is discarded, never shown, if a different account signs in there
>
> — `spec.md §5, AC-28, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `BoardScreen` — reads `openBoard` (or the seeded cache), renders SCR-08 on 404, the notice region, `KeptTextNotice` — `features/boards/BoardScreen.tsx` (replacing T18's placeholder).
- [ ] `BoardHeader` — back button, title through `PlainText`, owner-only rename (`InlineNameEditor`) and «Delete board» (calls an `onDeleteBoard` prop T22 wires) — `BoardHeader.tsx`.
- [ ] `InlineNameEditor` — input, hint line, refusal line, Save/Cancel, kept text — `InlineNameEditor.tsx` (reused by T21).
- [ ] `ColumnRow` + `BoardColumn` (tiles + add-card form only; T21 adds the header controls and drag) — `ColumnRow.tsx`, `BoardColumn.tsx`, `AddCardForm.tsx`; tiles call an `onOpenCard(cardId)` prop T22 wires.
- [ ] `BoardNotAvailable` — `BoardNotAvailable.tsx`.
- [ ] Component tests — `features/boards/__tests__/BoardScreen.test.tsx`.

## Edge cases

| Case | Behaviour |
|---|---|
| SCR-08 for non-member vs deleted vs never-existed | Identical DOM and `document.title` |
| A `Member` opens the board | No rename pencil, no «Delete board» |
| 404 on add card, board re-read 200 | Board refreshed, kept-text gone with the typed title and description |
| 20 columns on a narrow screen | Column row scrolls sideways; the page does not |
| Title with markup in a tile | Literal text |

## Definition of Done

- [ ] Component tests pass for every state above and every edge-case row.
- [ ] Every text from the server renders through `PlainText`.
- [ ] every Hard Rule inlined above still holds
- [ ] `npm --prefix src/Uniqua.Projector.Web run build` and `run lint` clean
