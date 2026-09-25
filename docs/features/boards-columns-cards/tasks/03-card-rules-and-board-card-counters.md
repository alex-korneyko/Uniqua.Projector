---
id: T3
title: "Admit, edit and delete cards through the Board's counters with the content-version check"
layer: "domain"
deps: ["T1"]
blocks: ["T8"]
acs: ["AC-12", "AC-13", "AC-14", "AC-15", "AC-18", "AC-23"]
files_hint:
  - "src/Uniqua.Projector.Domain/Boards/Board.cs"
  - "src/Uniqua.Projector.Domain/Boards/Column.cs"
  - "src/Uniqua.Projector.Domain/Boards/Card.cs"
  - "src/Uniqua.Projector.Domain/Boards/BoardError.cs"
  - "tests/Uniqua.Projector.Domain.Tests/Boards/CardTests.cs"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "todo"
---

# T3 — Admit, edit and delete cards through the Board's counters with the content-version check

## Place in the sequence

- **Blocked by:** T1 — Build the Board aggregate with the Text rule, board creation and the owner-only rules · **Blocks:** T8 — Write the card use cases: add, open, edit and delete through the board · **Wave:** 2.
- **Lane:** shares `Board.cs`, `Column.cs` and `BoardError.cs` with T1 and T2 — serialized with them (it may run before or after T2).

## Why (user story)

> **As a** board member
> **I want** to add a card with a title and an optional description to a column, and edit both later
> **So that** each piece of work is written down where everyone on the board can see it
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

> **As a** board member
> **I want** to delete a card that is no longer needed
> **So that** the board shows only work that is still real
>
> — `spec.md §4, US-05, verbatim` · full text: [spec.md](../spec.md)

This task lets the Board decide whether a card may be added or removed from its counts, and lets the Card decide whether its own edit is stale.

## Inlined context

> **The Board is the aggregate, the Card is not inside it.** The `Board` holds its columns (at most 20), its card count, each column's card count and next card position, and its version counters — everything a structural rule needs […]. Cards are a separate entity read and written one at a time, so a card change never loads 1,000 cards; the board decides whether a card may be added or a column deleted from its counts, and the `Card` decides whether its own edit is stale (ADR 0016).
>
> — `sad.md §5, Building block view, abridged` · full text: [sad.md](../sad.md)

> Cards hold ascending integers, a new card takes its column's next card position (kept on the column by the Board, one past the highest ever used), and a deletion leaves a gap.
>
> — `adr/0017, Considered options 1, abridged` · full text: [0017](../adr/0017-keep-column-positions-dense-and-card-positions-gapped.md)

> *editing or deleting a card* — that card's title or description was changed (a change to either counts, whichever the member is changing) (AC-23); […] *adding a column or a card* — never; it is placed at the end and overwrites nothing. Adding, editing or deleting a card is not a change to its column.
>
> — `spec.md §5, Stale-change rule, abridged` · full text: [spec.md](../spec.md)

> `boards.card_title_invalid` (empty after trimming or over 150 characters) or `boards.card_description_invalid` (over 10,000 characters). The whole card is refused (AC-14, flow 9), and the title is checked first.
>
> — `contracts/openapi.yaml, operationId addCard, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

> `Description` — Stored exactly as typed, never trimmed; ≤ 10,000 code points. **An empty string means no description**, so there is one representation of "none" and no NULL.
>
> — `data-model.md §Entities, table Cards, abridged` · full text: [data-model.md](../data-model.md)

> **Hard rule:** An invariant […] is enforced by the entity itself, never by a use case and never by an endpoint.
>
> — `CLAUDE.md §Domain rules live in Domain, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

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

### AC-14 — error

> **Given** a board member adding or editing a card
> **When** they submit a title that is empty once surrounding spaces are removed or longer than 150 characters, or a description longer than 10,000 characters
> **Then** the system refuses the whole change, tells them which limit was exceeded, and leaves what they typed in place
>
> — `spec.md §5, AC-14, verbatim` · full text: [spec.md](../spec.md)

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

### AC-23 — domain invariant

> **Given** a board member who opened a card, and that same card's title or description since changed — by another member, or by the same account in another tab or on another device
> **When** the first member saves their own change to that card, or deletes it
> **Then** the system refuses it, tells them the card was changed since they opened it, shows them the card as it is now, and keeps any text they typed so they can apply it again
>
> — `spec.md §5, AC-23, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `BoardError` additions: `CardTitleInvalid`, `CardDescriptionInvalid`, `CardLimitReached`, `CardChanged(currentCard)` — `Domain/Boards/BoardError.cs`.
- [ ] `Board.AdmitCard(columnId, title, description)` — column not on board → `NotAvailable`; title (trim, 1–150) then description (≤ 10,000 code points, untrimmed, absent → `""`); refuse at 1,000; `Position = column.NextCardPosition++`, `column.CardCount++`, `CardCount++`, `ContentVersion = 1`.
- [ ] `Card.Edit(title?, description?, seenContentVersion)` — Text rule on whichever is sent (title first), then stale (`ContentVersion != seen` → `CardChanged` with this card), then apply, `ContentVersion++`.
- [ ] `Card.EnsureDeletable(seenContentVersion)` and `Board.RemoveCard(card)` — stale first, then `column.CardCount--`, `CardCount--`; other cards' positions untouched.
- [ ] A card is reached only as "this card on this board": `card.BoardId != board.Id` → `NotAvailable`.
- [ ] Unit tests — `tests/Uniqua.Projector.Domain.Tests/Boards/CardTests.cs`.

## Edge cases

| Case | Behaviour |
|---|---|
| Title too long and description too long | `CardTitleInvalid` — title is checked first; nothing applied |
| Description of spaces and line breaks only | Accepted and stored exactly as typed |
| Edit sending only `description` after someone changed only the title | `CardChanged` — a change to either counts |
| Delete the last card, then add one | New card's position is one past the highest ever used, not 0 |
| 1,000th card | Accepted; 1,001st refused `CardLimitReached` |
| Card whose `BoardId` is another board | `NotAvailable` (AC-26) |

## Definition of Done

- [ ] Domain unit tests pass for every row above and each AC's domain decision.
- [ ] Deleting a card changes no other card's `Position` (asserted).
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
