---
id: T18
title: "Give the client board addresses with React Router, a guarded return address, and typed text kept across a sign-in"
layer: "ui"
deps: []
blocks: ["T19", "T20"]
acs: ["AC-27", "AC-28"]
files_hint:
  - "src/Uniqua.Projector.Web/package.json"
  - "src/Uniqua.Projector.Web/src/app/routes.tsx"
  - "src/Uniqua.Projector.Web/src/App.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/AccountShell.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/SignInScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/draftStore.ts"
  - "src/Uniqua.Projector.Web/src/features/boards/BoardListScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/BoardScreen.tsx"
  - "src/Uniqua.Projector.Web/src/app/__tests__/routes.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/draftStore.test.ts"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T18 — Give the client board addresses with React Router, a guarded return address, and typed text kept across a sign-in

## Place in the sequence

- **Blocked by:** nothing — it starts in wave 1 · **Blocks:** T19 — Build the board list (SCR-02) and the create-board dialog (SCR-03), T20 — Build the board screen (SCR-04) with its owner header, card tiles and add-card form, and the not-available screen (SCR-08) · **Wave:** 1 — parallel with the backend and with T17.
- **Lane:** shares `package.json` with T16 — serialized with it.

## Why (user story)

> **As a** visitor
> **I want** a link to a board to send me to sign in without revealing anything about that board
> **So that** a link that reaches the wrong person gives nothing away
>
> — `spec.md §4, US-10, verbatim` · full text: [spec.md](../spec.md)

This task gives a board an address of its own and makes sure a link, and a session that ends mid-change, give nothing away and lose nothing typed.

## Inlined context

> **Chosen:** Option 1 [React Router, library mode — `react-router` (MIT) as a client router only: routes for the board list, `/boards/:boardId` and sign-in; data loading stays in TanStack Query]. […] library mode keeps it to routing — its loader and action features are deliberately left unused so TanStack Query stays the only server-state owner.
>
> — `adr/0012, Considered options + Decision outcome, abridged` · full text: [0012](../adr/0012-route-the-web-client-with-react-router-in-library-mode.md)

> | Sign-in return address | `returnTo` is honoured only when it is a path inside the application — starts with a single `/`, not `//`, no scheme; anything else lands on the board list |
> | Typed text across sign-in | When a change is refused because the session ended, what the member typed is kept in `sessionStorage` under the id of the account that typed it; it is offered back only if that same account signs in again in this tab, and is removed when re-applied, when a different account signs in (never shown), or on sign-out. It never outlives the tab |
>
> — `sad.md §8, crosscutting rows, verbatim` · full text: [sad.md](../sad.md)

> SCR-01 states: **from-board-link** — No session, at `/boards/:boardId`, real or not: same as default, pixel for pixel. The address is remembered as `returnTo`. No board request is made, and nothing about the board is shown. **from-ended-session** — A change answered 401 while a board was open: same as default, with no extra message. **success** — `returnTo` is followed only when it is an in-app path […]. Otherwise, or when there is none, the member lands on SCR-02. Kept text belonging to a different account is removed from storage and never shown.
>
> — `screens.md §SCR-01, abridged` · full text: [screens.md](../screens.md)

> **Hard rule:** The API serves the built client from `wwwroot`, and the Vite dev server proxies API calls to it. […] Any hosting proposal that separates them breaks authentication.
>
> — `CLAUDE.md §One origin, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read [screens.md](../screens.md) SCR-01, [sad.md](../sad.md) flows 12–13, and `features/auth/AccountShell.tsx` in full and follow them. Do not guess.

## Data delta

No DB changes.

## API contract

Uses only the inherited session read (`getCurrentAccount`) and sign-in from accounts-and-sessions; no board request is made without a session. Any 401 `accounts.session_not_recognised` on a board change is the trigger for keeping text.

— `contracts/openapi.yaml, components.responses.SessionNotRecognised, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-27 — cross-context

> **Given** a visitor with no active session
> **When** they open a link to a board
> **Then** the system presents the sign-in form and reveals nothing about the board — not its name and not whether it exists — answering a link to a real board exactly as it answers one to a board that never existed; once they sign in, they are returned to that link's address and answered there as any signed-in account is — the board if they are a member, otherwise the refusal of AC-25
>
> — `spec.md §5, AC-27, verbatim` · full text: [spec.md](../spec.md)

### AC-28 — cross-context

> **Given** a board member whose session has ended — they signed out on this browser, or it expired — while the board is still open in front of them
> **When** they submit a change
> **Then** the system changes nothing on the board, presents the sign-in form, and does not treat the change as coming from that member; what they typed is kept in this browser so that, once they sign in again as the same account, they can apply it again, and it is discarded, never shown, if a different account signs in there
>
> — `spec.md §5, AC-28, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Add `react-router` (MIT) — `package.json`; `BrowserRouter` with `/` → `BoardListScreen` and `/boards/:boardId` → `BoardScreen`, both created here as placeholders that T19 and T20 fill in, so neither of them edits the route table; no loaders, no actions — `src/app/routes.tsx`, `App.tsx`, `features/boards/BoardListScreen.tsx`, `features/boards/BoardScreen.tsx`.
- [ ] `AccountShell`: with no session at any address, render `VisitorScreens` and remember the current path as `returnTo`; after sign-in or registration, follow `returnTo` only through `isInAppPath(path)` — `features/auth/AccountShell.tsx`, `SignInScreen.tsx`.
- [ ] `isInAppPath` — true only for `/x…` not starting `//` or `/\`, and no scheme.
- [ ] `draftStore` — `keep(accountId, {boardId?, item, fields})`, `takeFor(accountId)`, `discardUnlessOwnedBy(accountId)`, `clearAll()` on sign-out; `sessionStorage` only — `features/boards/draftStore.ts`.
- [ ] Tests — `src/app/__tests__/routes.test.tsx`, `features/boards/__tests__/draftStore.test.ts`.

## Edge cases

| Case | Behaviour |
|---|---|
| `returnTo=//evil.example` or `https://evil.example` or `/\evil` | Ignored → board list |
| Visitor at `/boards/never-existed` vs a real board id | Identical sign-in form; no network request naming the board |
| Account B signs in where account A's text was kept | Text removed, never rendered |
| Tab closed before signing in | Text gone — accepted, sad §11 (tab, not browser) |
| Sign-out | All kept text removed |

## Definition of Done

- [ ] Component and unit tests pass for every row above.
- [ ] A test asserts no `fetch` to `/api/v1/boards/…` happens while no session is recognised.
- [ ] every Hard Rule inlined above still holds
- [ ] `npm --prefix src/Uniqua.Projector.Web run build` and `run lint` clean
