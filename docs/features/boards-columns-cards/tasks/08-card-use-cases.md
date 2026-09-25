---
id: T8
title: "Write the card use cases: add, open, edit and delete through the board"
layer: "app"
deps: ["T3", "T5"]
blocks: ["T11"]
acs: ["AC-12", "AC-13", "AC-15", "AC-18", "AC-18b", "AC-23", "AC-26"]
files_hint:
  - "src/Uniqua.Projector.Application/Boards/AddCard.cs"
  - "src/Uniqua.Projector.Application/Boards/OpenCard.cs"
  - "src/Uniqua.Projector.Application/Boards/EditCard.cs"
  - "src/Uniqua.Projector.Application/Boards/DeleteCard.cs"
  - "src/Uniqua.Projector.Application/DependencyInjection.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/CardUseCaseTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T8 — Write the card use cases: add, open, edit and delete through the board

## Place in the sequence

- **Blocked by:** T3 — Admit, edit and delete cards through the Board's counters with the content-version check, T5 — Declare the board ports and implement the member-scoped store with the bounded concurrency retry · **Blocks:** T11 — Expose the card endpoints: add, open, edit and delete · **Wave:** 4.
- **Lane:** shares `Application/DependencyInjection.cs` with T6 and T7 — serialized with them.

## Why (user story)

> **As a** board member
> **I want** to add a card with a title and an optional description to a column, and edit both later
> **So that** each piece of work is written down where everyone on the board can see it
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

> **As a** board member
> **I want** a change I make from an outdated view to be refused and explained rather than applied
> **So that** no one's work disappears without anyone noticing
>
> — `spec.md §4, US-08, verbatim` · full text: [spec.md](../spec.md)

This task orchestrates the card actions — the only path to a card is through the board its membership was checked on.

## Inlined context

> `App->>Infra: Read the card through this board` → `Infra-->>App: The card at its current content version, or nothing` → `App->>Domain: Apply the edit at version N` → […] accepted: `App->>Infra: Save the card only if it is still at version N` → `Written - or no row when another save landed first, and then the member gets the stale refusal with the card as it is now`.
>
> — `sad.md §6, flow 2, abridged` · full text: [sad.md](../sad.md)

> Add accepted: `App->>Infra: Insert the card and save the board only if the board is still at its token` — persists the card, and on the board the card count, the column's card count and its next card position — `Written, or a conflict - then reload and re-decide, so two adds at 999 cards cannot both land`.
>
> — `sad.md §6, flow 9, abridged` · full text: [sad.md](../sad.md)

> Delete unchanged since N: `Delete the card only if it is still at N, and save the board only if the board is still at its token` — `Written, or no card row at N - another edit landed first, then the stale branch answers - or a board conflict, then reload and re-decide`.
>
> — `sad.md §6, flow 10, abridged` · full text: [sad.md](../sad.md)

> **Chosen:** Option 1 [summaries, text on demand] — opening a card fetches its full text. […] card detail always shows current text, which is what AC-23's comparison is made against.
>
> — `adr/0018, Decision outcome, abridged` · full text: [0018](../adr/0018-open-a-board-with-card-summaries-and-load-a-cards-text-on-demand.md)

> **Hard rule:** A use case orchestrates; it does not decide what is legal.
>
> — `CLAUDE.md §Domain rules live in Domain, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([sad.md](../sad.md) · [data-model.md](../data-model.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes — inserts / updates / deletes `Cards` rows; updates `Boards.CardCount` / `RowVersion` and `Columns.CardCount` / `NextCardPosition` on add and delete.

## API contract

Internal — no API surface; T11 exposes these as `addCard` (→ `CardSummary`), `openCard` and `editCard` (→ `Card`: `id`, `column_id`, `position`, `title`, `description`, `content_version`), `deleteCard`.

— `contracts/openapi.yaml, components.schemas.CardSummary + Card, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-12 — happy path

> **Given** a board member on a board holding fewer than 1,000 cards
> **When** they add a card to a column with a title of 1 to 150 characters and, optionally, a description of up to 10,000 characters
> **Then** the system adds the card at the end of that column and shows its title on the board
>
> — `spec.md §5, AC-12, verbatim` · full text: [spec.md](../spec.md)

### AC-13 — happy path

> **Given** a board member viewing a card
> **When** they change its title, its description, or both, and save
> **Then** the system records the change, and every member who next opens the card sees the new text
>
> — `spec.md §5, AC-13, verbatim` · full text: [spec.md](../spec.md)

### AC-15 — domain invariant

> **Given** a board member on a board that already holds 1,000 cards
> **When** they try to add another card to any of its columns
> **Then** the system refuses and tells them a board can hold at most 1,000 cards — and the limit holds even when several cards are added at the same moment
>
> — `spec.md §5, AC-15, verbatim` · full text: [spec.md](../spec.md)

### AC-18 — happy path

> **Given** a board member viewing a card
> **When** they delete it and confirm
> **Then** the system removes the card, the other cards in its column keep their relative order, and a member who then tries to change that card is refused exactly as for a card that never existed
>
> — `spec.md §5, AC-18, verbatim` · full text: [spec.md](../spec.md)

### AC-18b — edge case

> **Given** a board member whose view still shows a column or card that has since been deleted
> **When** they submit a change that names it — editing or deleting that card, renaming or deleting that column, or adding a card to that column
> **Then** the system refuses it exactly as it refuses a column or card that never existed, changes nothing on the board, and keeps what they typed in place
>
> — `spec.md §5, AC-18b, verbatim` · full text: [spec.md](../spec.md)

### AC-23 — domain invariant

> **Given** a board member who opened a card, and that same card's title or description since changed — by another member, or by the same account in another tab or on another device
> **When** the first member saves their own change to that card, or deletes it
> **Then** the system refuses it, tells them the card was changed since they opened it, shows them the card as it is now, and keeps any text they typed so they can apply it again
>
> — `spec.md §5, AC-23, verbatim` · full text: [spec.md](../spec.md)

### AC-26 — authorization

> **Given** a board member of one board, and any other board — whether or not they are also a member of it
> **When** they submit a change to the first board that names a column or a card belonging to the other board
> **Then** the system refuses it exactly as if that column or card did not exist, and changes nothing on either board
>
> — `spec.md §5, AC-26, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `AddCard` — load → `Board.AdmitCard` → insert + save board, inside `BoardChangeRetry` — `Application/Boards/AddCard.cs`.
- [ ] `OpenCard` — load → `FindCardAsync(boardId, cardId)` → null ⇒ `NotAvailable` — `OpenCard.cs`.
- [ ] `EditCard` — load → find → `Card.Edit` → save conditional on `ContentVersion`; lost write → reload → `CardChanged` with the current card — `EditCard.cs`.
- [ ] `DeleteCard` — load → find → `Card.EnsureDeletable` + `Board.RemoveCard` → delete conditional on `ContentVersion`, board saved under its token, inside the retry — `DeleteCard.cs`.
- [ ] Register in `AddApplication` — `src/Uniqua.Projector.Application/DependencyInjection.cs`.
- [ ] Integration tests — `tests/.../Boards/CardUseCaseTests.cs`.

## Edge cases

| Case | Behaviour |
|---|---|
| Card id of a board the caller is also a member of, sent with this board | `NotAvailable`; nothing changed on either board |
| Two adds at 999 cards, forced to collide | One card added, the other `CardLimitReached` |
| Edit races with a delete of the same card | Whichever lands second gets `CardChanged` or `NotAvailable`; never both applied |
| Change to a deleted card | `NotAvailable` — identical to never-existed (AC-18) |
| Edit after a *different* card changed | Accepted (AC-24b) |

## Definition of Done

- [ ] Use-case integration tests pass for each AC above and each edge-case row.
- [ ] Every one of the four begins with `LoadForMemberAsync`; no card query omits `BoardId`.
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
