---
id: T19
title: "Build the board list (SCR-02) and the create-board dialog (SCR-03)"
layer: "ui"
deps: ["T16", "T17", "T18"]
blocks: []
acs: ["AC-01", "AC-02", "AC-03", "AC-04"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/boards/BoardListScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/CreateBoardDialog.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/BoardListScreen.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/CreateBoardDialog.test.tsx"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T19 — Build the board list (SCR-02) and the create-board dialog (SCR-03)

## Place in the sequence

- **Blocked by:** T16 — Vendor Dialog and Textarea, add the destructive Button variant, and build PlainText and KeptTextNotice, T17 — Write the typed boards API client, its query keys and cache patches, and the board refusal wording, T18 — Give the client board addresses with React Router, a guarded return address, and typed text kept across a sign-in · **Blocks:** nothing — it is a leaf · **Wave:** 2 — runs beside T20.
- **Lane:** own lane — T18 already routes to `BoardListScreen`, so this task only fills it in.

## Why (user story)

> **As an** account
> **I want** to create a board with a name and have it ready to use
> **So that** I can start organising work without setting anything up first
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

> **As an** account
> **I want** to see every board I am a member of, and only those
> **So that** I can get back to my work and never stumble onto someone else's
>
> — `spec.md §4, US-02, verbatim` · full text: [spec.md](../spec.md)

This task is where a first-time visitor starts the thin path: an empty list that offers «Create board», and a dialog that opens the new board.

## Inlined context

> **SCR-02 — Board list.** Root: `BoardListScreen`, inside `AccountShell`'s signed-in frame. `AccountShell`'s `max-w-md` body is widened for the board screens.
>
> | State | Trigger / condition | Components |
> |---|---|---|
> | loading | `listMyBoards` in flight | status line «Loading your boards…» |
> | empty | 200 with `items: []` | `Card` with «You have no boards yet.» and a `Button` (default) «Create board» → SCR-03 |
> | default | 200 with one or more items (AC-04) | Heading «My boards» and a `Button` «Create board». Ordered newest first by `created_at`, as the server sends it. Each row is a `Button` (outline, full width, left-aligned) that goes to `/boards/:id`. The name is shown through `PlainText`, with a muted «Owner» marker when `is_owner` |
> | error | 500, or no answer | failed block «We could not load your boards» + `Button` «Try again» (refetch) |
> | session-ended | 401 on the read | SCR-01 `from-board-link` with `returnTo` = `/` |
> | kept-text offer | Back on the list after SCR-01, same account, with a kept board name from SCR-03 (AC-28) | `KeptTextNotice` «Apply again» (reopens SCR-03 with the name filled in) / «Discard» |
> | after-delete | Arrives from SCR-07 `success` (AC-20) | The deleted board already removed from the cached list. No toast |
>
> — `screens.md §SCR-02, abridged` · full text: [screens.md](../screens.md)

> **SCR-03 — Create board.** `Dialog` titled «Create board»; `Label` «Board name» + `Input` (autofocused) + hint line «Between 1 and 100 characters.»; «Create» / «Cancel». pending: «Creating…», disabled. validation: 400 `boards.board_name_invalid` — refusal line under the input, typed text stays, focus returns to the input; the client may run the same check first (code points after trimming Unicode whitespace). limit: 409 `boards.owned_board_limit_reached` — refusal line, «Create» disabled, «Cancel» the only way on. rate-limited / busy / error: refusal line, name kept. session-ended: → SCR-01 `from-ended-session`, name kept. success: 201 `Board` — dialog closes, go to `/boards/:id`, the board query seeded from the 201 body so SCR-04 shows To do, In progress, Done with no second request.
>
> — `screens.md §SCR-03, abridged` · full text: [screens.md](../screens.md)

> Idioms: **status line** `<div role="status" aria-live="polite">` `text-muted-foreground text-sm`; **refusal line** `<p role="alert" className="text-destructive text-sm">` under the control acted on; **hint line** bound with `aria-describedby`; **failed block** heading + sentence + «Try again».
>
> — `screens.md §Source, Idioms, abridged` · full text: [screens.md](../screens.md)

> **Hard rule:** Tailwind utility classes are the only styling mechanism in this repository. […] TanStack Query owns all server state.
>
> — `CLAUDE.md §Styling: Tailwind, once, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read [screens.md](../screens.md) SCR-02/SCR-03 and the auth screens in full and follow them. Do not guess.

## Data delta

No DB changes.

## API contract

`listMyBoards` → `BoardListPage.items[{id, name, created_at, is_owner}]`; `createBoard {name}` → 201 `Board`, refusals `board_name_invalid` (400), `owned_board_limit_reached` (409), `change_rate_limited` (429), `contended` (503) — through T17's client.

— `contracts/openapi.yaml, operationIds listMyBoards + createBoard, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-01 — happy path

> **Given** an account signed in
> **When** they create a board and give it a name of 1 to 100 characters
> **Then** the system creates the board with that name, makes the account its board owner and only member, gives it three columns named To do, In progress and Done in that order, and opens it; a board name need not be unique, even among one account's boards
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-02 — error

> **Given** an account creating a board
> **When** they submit a name that is empty once surrounding spaces are removed, or longer than 100 characters
> **Then** the system refuses to create the board and tells them the name must be between 1 and 100 characters, leaving what they typed in place
>
> — `spec.md §5, AC-02, verbatim` · full text: [spec.md](../spec.md)

### AC-03 — domain invariant

> **Given** an account that already owns 50 boards
> **When** they try to create another
> **Then** the system refuses and tells them an account can own at most 50 boards, because each account's share of the product is bounded — and the limit holds even when several boards are created at the same moment
>
> — `spec.md §5, AC-03, verbatim` · full text: [spec.md](../spec.md)

### AC-04 — happy path

> **Given** an account that owns some boards and is a member of others
> **When** they open their list of boards
> **Then** the system lists every board they are a member of, most recently created first, marks the ones they own, and lists no other board
>
> — `spec.md §5, AC-04, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `BoardListScreen` — every SCR-02 state, rows through `PlainText`, `KeptTextNotice` from `draftStore` — `features/boards/BoardListScreen.tsx`.
- [ ] Widen `AccountShell`'s signed-in body for board screens without changing the auth screens.
- [ ] `CreateBoardDialog` — every SCR-03 state, client-side Text rule check by code point, keep text on every refusal, `draftStore.keep` on 401 — `features/boards/CreateBoardDialog.tsx`.
- [ ] Component tests — `features/boards/__tests__/`.

## Edge cases

| Case | Behaviour |
|---|---|
| Name of 100 emoji | Accepted client-side (code points, not UTF-16 length) |
| Name of only spaces | Client refuses before sending, same message as the server's |
| 409 limit | «Create» stays disabled until the dialog is closed |
| A board named `<script>alert(1)</script>` in the list | Shown literally |

## Definition of Done

- [ ] Component tests pass for every state above.
- [ ] Every refusal keeps the typed name and moves focus to the input.
- [ ] every Hard Rule inlined above still holds
- [ ] `npm --prefix src/Uniqua.Projector.Web run build` and `run lint` clean
