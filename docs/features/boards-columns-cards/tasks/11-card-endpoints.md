---
id: T11
title: "Expose the card endpoints: add, open, edit and delete"
layer: "ports"
deps: ["T8", "T9"]
blocks: ["T13", "T14", "T15"]
acs: ["AC-12", "AC-13", "AC-14", "AC-15", "AC-16", "AC-18", "AC-23"]
files_hint:
  - "src/Uniqua.Projector.Api/Boards/BoardEndpoints.Cards.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardEndpoints.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardProblems.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Fixtures/BoardFixtures.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/CardEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T11 — Expose the card endpoints: add, open, edit and delete

## Place in the sequence

- **Blocked by:** T8 — Write the card use cases: add, open, edit and delete through the board, T9 — Expose the board endpoints with lenient binding and map every board refusal to one problem document · **Blocks:** T13 — Prove the membership boundary across every read and change kind, and map the four board rules to their tests, T14 — Prove the board invariants under 1,000 simultaneous pairs and the column order under 1,000 random sequences, T15 — Measure opening a full board, a single change and change throughput against the §6 budgets · **Wave:** 6.
- **Lane:** shares `BoardEndpoints.cs`, `BoardProblems.cs` and `BoardFixtures.cs` with T9, T10, T12 — serialized with them.

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

This task puts the card actions on the wire and returns card text exactly as stored.

## Inlined context

> All board, column and card text is plain text. It is returned exactly as stored, and a client renders it only as text, never as markup or links (AC-16).
>
> — `contracts/openapi.yaml, info.description, verbatim` · full text: [openapi.yaml](../contracts/openapi.yaml)

> Send `title`, `description`, or both, plus the `content_version` of the card as it was opened. The edit is stale if either the title or the description changed since, whichever one this edit touches. […] An edit with neither field is `boards.request_invalid`.
>
> — `contracts/openapi.yaml, operationId editCard, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

> `ABoardWithCardsAsync(…, int cardCount)` seeds the board up to one short of the ceiling (999 cards) […]. They insert directly, because 999 API calls would run into the 120-per-minute limit, and they keep `Boards.CardCount`, `Columns.CardCount` and `NextCardPosition` consistent with the rows they insert.
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
| `POST /api/v1/boards/{boardId}/cards` `addCard` | `{column_id, title, description?}` | 201 `CardSummary {id, column_id, position, title, content_version}` | 400 `card_title_invalid` → `card_description_invalid`; 409 `card_limit_reached`; 503 |
| `GET …/cards/{cardId}` `openCard` | — | 200 `Card` (+ `description`) | 404 |
| `PATCH …/cards/{cardId}` `editCard` | `{title?, description?, content_version}` | 200 `Card` | 400 as add; 409 `card_changed` + `current_card` (no 503) |
| `DELETE …/cards/{cardId}` `deleteCard` | `{content_version}` | 204 | 409 `card_changed` + `current_card`; 503 |

All four: 400 `request_malformed` (before membership) / `request_invalid` (after); 404 `not_available` for an absent board, a non-member, or a column or card not on this board.

— `contracts/openapi.yaml, paths …/cards*, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

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

### AC-16 — domain invariant

> **Given** a board member who writes a card title or description containing markup, a script or other formatting syntax
> **When** any member views that card
> **Then** the system shows exactly the characters that were typed, as plain text, and nothing in them is ever run or rendered as formatting — line breaks and repeated spaces are shown as typed, and a web address stays plain text rather than becoming a link
>
> — `spec.md §5, AC-16, verbatim` · full text: [spec.md](../spec.md)

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

- [ ] `MapCardEndpoints` on the board group — `Api/Boards/BoardEndpoints.Cards.cs`, called from `BoardEndpoints.cs`.
- [ ] Read `column_id`, `title`, `description`, `content_version` through `BoardRequestBody` after the load; absent `description` on add → `""`; neither field on edit → `request_invalid`.
- [ ] Map `CardTitleInvalid`, `CardDescriptionInvalid`, `CardLimitReached`, `CardChanged` (+ `current_card`) — `Api/Boards/BoardProblems.cs`.
- [ ] `ABoardWithCardsAsync` — `tests/.../Fixtures/BoardFixtures.cs`.
- [ ] Endpoint tests — `tests/.../Boards/CardEndpointTests.cs`.

## Edge cases

| Case | Behaviour |
|---|---|
| Title `<script>alert(1)</script>`, description with `\n`, double spaces and a URL | Stored and returned byte for byte; `Content-Type: application/json` |
| `column_id` of another board | 404 `not_available` |
| Edit `{content_version: 1}` only | 400 `request_invalid` |
| Non-member sends a stale `content_version` | 404 — no `current_card` ever reaches a non-member |
| Delete of an already deleted card | 404 `not_available`, identical to never-existed |

## Definition of Done

- [ ] Endpoint integration tests pass for every AC above and every edge-case row, including one pass as a `Member`.
- [ ] Response bodies validate against `CardSummary` and `Card` (no extra members; no description in `CardSummary`).
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
