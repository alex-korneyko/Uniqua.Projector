---
id: T22
title: "Build the card detail dialog with edit and delete (SCR-05, SCR-06) and the board deletion dialog (SCR-07)"
layer: "ui"
deps: ["T20"]
blocks: []
acs: ["AC-13", "AC-14", "AC-16", "AC-18", "AC-20", "AC-20b", "AC-23"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/boards/BoardScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/CardDetailDialog.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/DeleteBoardDialog.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/CardDetailDialog.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/DeleteBoardDialog.test.tsx"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T22 — Build the card detail dialog with edit and delete (SCR-05, SCR-06) and the board deletion dialog (SCR-07)

## Place in the sequence

- **Blocked by:** T20 — Build the board screen (SCR-04) with its owner header, card tiles and add-card form, and the not-available screen (SCR-08) · **Blocks:** nothing — it is a leaf · **Wave:** 3 — the client branch's last task, beside T21.
- **Lane:** own lane — it wires the `onOpenCard` / `onDeleteBoard` props T20 left in `BoardScreen.tsx`; T21 does not touch that file.

## Why (user story)

> **As a** board member
> **I want** to delete a card that is no longer needed
> **So that** the board shows only work that is still real
>
> — `spec.md §4, US-05, verbatim` · full text: [spec.md](../spec.md)

> **As a** board owner
> **I want** to rename the board, and to delete it with everything on it once I confirm
> **So that** the board's identity and existence stay under the control of the person who created it
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

This task lets a member read, edit and delete a card, and the owner delete the board by typing its name — each refusing a change made from an outdated view.

## Inlined context

> **SCR-05 — Card detail.** `CardDetailDialog`, a `Dialog` over SCR-04, no address of its own. loading — titled with the summary title, status line «Loading the card…». default — title as `PlainText` in the heading; description as `PlainText` in a scrollable body, line breaks and repeated spaces kept, web addresses not linked; empty description → muted «No description»; «Edit», «Delete card» → SCR-06. error — failed block «We could not load this card». gone (read) — 404, re-read: board available → dialog closes, SCR-04 refreshed, «That card no longer exists.»; else SCR-08. editing — `Input` «Title» + `Textarea` «Description» with hints; «Save» / «Cancel». validation — refusal line under the failing field, both kept. stale — 409 `boards.card_changed` + `current_card`: card query and tile patched; a `Card` block «Current version» above the editor; refusal line «The card was changed. This card was changed since you opened it.»; typed text stays, «Save» applies it against the new `content_version`. gone (save) — kept-text gone with the typed title and description. busy — N/A on `editCard`.
>
> **SCR-06 — Confirm card deletion.** A step inside `CardDetailDialog` (no second dialog). «Delete this card?», the title as `PlainText`, «This cannot be undone.», `Button` (`destructive`) «Delete card», «Cancel». stale — back to reading, patched, refusal line. gone — dialog closes, board refreshed. success 204 — the tile leaves its column, others keep their order. session-ended — nothing to keep.
>
> **SCR-07 — Confirm board deletion.** `DeleteBoardDialog`, only when `is_owner`. «This deletes the board with all its columns and cards. It cannot be undone.», the name as `PlainText` in bold, `Label` «Type the board's name to confirm» + `Input`, `Button` (`destructive`) «Delete board» **always enabled**, because the server compares the name. mismatch — 409 `boards.confirmation_mismatch` + `current_name`: refusal line, displayed name replaced by `current_name`, board cache patched, typed text stays. owner-only — refusal line; dialog closes when the re-read returns `is_owner: false`. not-available — SCR-08. success 204 — to SCR-02, board removed from the list cache, its query dropped. session-ended — typed name kept; «Apply again» only reopens this dialog, never deletes on its own.
>
> — `screens.md §SCR-05, SCR-06, SCR-07, abridged` · full text: [screens.md](../screens.md)

> The card's summary (in the board cache) and its detail (in the card cache) must agree; every accepted card change patches both from the server's answer, and every stale refusal patches both from the current state it carries.
>
> — `adr/0018, Consequences (Negative), verbatim` · full text: [0018](../adr/0018-open-a-board-with-card-summaries-and-load-a-cards-text-on-demand.md)

> **Hard rule:** Tailwind utility classes are the only styling mechanism […]. TanStack Query owns all server state.
>
> — `CLAUDE.md §Styling: Tailwind, once, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read [screens.md](../screens.md) SCR-05 to SCR-07 in full and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

`openCard` → `Card {id, column_id, position, title, description, content_version}`; `editCard {title?, description?, content_version}` → `Card` / 409 `card_changed` + `current_card`; `deleteCard {content_version}` → 204 / 409 `card_changed`; `deleteBoard {confirm_name}` → 204 / 409 `confirmation_mismatch` + `current_name` / 403 `owner_only` / 404.

— `contracts/openapi.yaml, operationIds openCard + editCard + deleteCard + deleteBoard, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

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

### AC-20 — happy path

> **Given** the board owner viewing their board
> **When** they choose to delete it and confirm by entering the board's current name exactly — the same letters in the same case, after the Text rule's trimming
> **Then** the system itself checks that confirmation, deletes the board together with all its columns and cards, and from then on answers any request about that board, or anything that was on it, exactly as it answers for a board that never existed
>
> — `spec.md §5, AC-20, verbatim` · full text: [spec.md](../spec.md)

### AC-20b — error

> **Given** the board owner deleting their board
> **When** the name they enter does not match the board's current name — including because the board was renamed since they opened it
> **Then** the system refuses, deletes nothing, and shows them the board's current name
>
> — `spec.md §5, AC-20b, verbatim` · full text: [spec.md](../spec.md)

### AC-23 — domain invariant

> **Given** a board member who opened a card, and that same card's title or description since changed — by another member, or by the same account in another tab or on another device
> **When** the first member saves their own change to that card, or deletes it
> **Then** the system refuses it, tells them the card was changed since they opened it, shows them the card as it is now, and keeps any text they typed so they can apply it again
>
> — `spec.md §5, AC-23, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `CardDetailDialog` — reading / editing / confirm-delete steps in one `Dialog`, every SCR-05 and SCR-06 state — `features/boards/CardDetailDialog.tsx`.
- [ ] `DeleteBoardDialog` — every SCR-07 state; the typed name is sent as typed (the server trims and compares) — `DeleteBoardDialog.tsx`.
- [ ] Wire `onOpenCard` and `onDeleteBoard` in `BoardScreen.tsx`.
- [ ] All changes through `useBoardChange` (T17) with the last-seen `content_version`; stale answers patch both caches.
- [ ] Component tests — `features/boards/__tests__/`.

## Edge cases

| Case | Behaviour |
|---|---|
| Description `<b>not bold</b>\nTwo   spaces https://example.com` | Literal, line break and spaces kept, no link |
| Stale edit, then Save again | Sent with `current_card.content_version`; accepted |
| Delete board typed `q4 launch` for `Q4 launch` | Sent; server's mismatch shown; nothing deleted |
| Board renamed in another tab while SCR-07 is open | Mismatch shows the new name via `current_name` |
| Esc while editing | Closes the dialog; the board is as it was |

## Definition of Done

- [ ] Component tests pass for every state above and every edge-case row.
- [ ] Card text renders only through `PlainText`.
- [ ] every Hard Rule inlined above still holds
- [ ] `npm --prefix src/Uniqua.Projector.Web run build` and `run lint` clean
