---
id: T15
title: "Build the web API client, the session bootstrap and the signed-in shell with sign-out"
layer: "ui"
deps: []
blocks: ["T16", "T17"]
acs: ["AC-06", "AC-08", "AC-10", "AC-11"]
files_hint:
  - "src/Uniqua.Projector.Web/src/api/accounts.ts"
  - "src/Uniqua.Projector.Web/src/features/auth/useSession.ts"
  - "src/Uniqua.Projector.Web/src/features/auth/AccountShell.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/__tests__/"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "done"
---

# T15 — Build the web API client, the session bootstrap and the signed-in shell with sign-out

## Place in the sequence

- **Blocked by:** — (wave 1) · **Blocks:** T16 — registration screen, T17 — sign-in screen · **Wave:** 1. It is the head of the UI branch, which runs in parallel with the whole backend branch: it is built against `contracts/openapi.yaml` and tested against a mocked transport, so it does not wait for the endpoints.
- **Lane:** own lane — `layer: ui` is not auto-serialized, and no other task touches `src/Uniqua.Projector.Web/src/api/` or `features/auth/useSession.ts`.

## Why (user story)

> **As an** account
> **I want** signing out to end this browser's session across every channel at once
> **So that** leaving a shared machine does not leave the product open behind me
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

This task delivers the client's whole notion of "am I signed in": one call on load, one shell that shows the account's own display name, and one control that ends the session.

## Inlined context

**No `screens.md` exists for this feature**, and no `SCR-NN` id can be cited. The states below are derived from the §5 acceptance criteria and the contract's error responses, which is exactly what ADR 0006 and the sad §11 risk row say will happen when `ux-flows` and `screens` are skipped:

> `ux-flows` was skipped, so `screens` will derive screen states from acceptance criteria and contract error responses rather than from a screen inventory. Mitigation: run `/sdd:ux-flows accounts-and-sessions` before `screens` if the error-state coverage looks thin.
>
> — `sad.md §11, risk row, verbatim` · full text: [sad.md](../sad.md)

> The authentication screens reuse the vendored shadcn/ui primitives and Tailwind tokens already inventoried in `architecture-map.md` §Frontend, rather than introducing their own.
>
> — `adr/0007-deliver-the-web-surface-as-a-client-side-spa.md §Consequences, positive, verbatim` · full text: [adr/0007](../adr/0007-deliver-the-web-surface-as-a-client-side-spa.md)

> **Reuse, do not reinvent.** Component library: shadcn/ui, vendored as source — `src/Uniqua.Projector.Web/src/components/ui/`. Design tokens: Tailwind theme plus CSS custom properties — `src/Uniqua.Projector.Web/src/index.css` and `tailwind.config.ts`. Styling approach: Tailwind utility classes; this is the only styling mechanism in the repository — a second one is a review finding, not a preference. Shared primitives: Button, Input, Dialog, DropdownMenu, Card, Avatar. State / data-fetching: TanStack Query owns all server state.
>
> — `architecture-map.md §Frontend / UI foundation, abridged` · full text: [architecture-map.md](../../../architecture-map.md)

**Components reused:** `Button`, `Card`, `Avatar`, `DropdownMenu` from the vendored set, styled with the Tailwind tokens. **No new primitive is introduced by this task** — and no `docs/design-system.md` exists yet to register one in, so if one turns out to be unavoidable, stop and run `/sdd:design-system` rather than inventing a second inventory.

> `GET /api/v1/accounts/me` — the client calls it on load to decide whether to show the account or the sign-in form. A session that is absent, revoked (AC-08/AC-10), idle for 14 days (AC-07) or opened more than 90 days ago (AC-07b) is refused here with one and the same code — the client presents the sign-in form regardless of what the browser still holds.
>
> — `contracts/openapi.yaml, operationId getCurrentAccount description, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

> **Hard rule:** The session reference is never present in a request or response body: page scripts cannot read it, and this contract deliberately offers no way for them to.
>
> — `contracts/openapi.yaml, info.description, verbatim` · full text: [openapi.yaml](../contracts/openapi.yaml)

> **Hard rule — risk:** The SPA shows a blank page until its bundle loads, and spec §1's primary reader gives the link about one minute. Mitigation: keep the bundle small.
>
> — `sad.md §11, risk row, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([openapi.yaml](../contracts/openapi.yaml) · [spec.md](../spec.md) · [architecture-map.md](../../../architecture-map.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

- `GET /api/v1/accounts/me` → `200` `Account` (`id`, `email`, `display_name`) · `401 accounts.session_not_recognised`.
- `DELETE /api/v1/sessions/current` → `204` · `401 accounts.session_not_recognised`, `403 accounts.antiforgery_failed`.
- Every state-changing call sends `X-XSRF-TOKEN`. How the token is obtained is **OQ-API-1** — open, owner `sequences`; take the shape T11 implemented rather than inventing a second one.
- The client never reads, stores or sends the session reference itself: the browser attaches the httpOnly cookie on its own, so requests are made with credentials included and nothing else.

— `contracts/openapi.yaml, operationIds getCurrentAccount + deleteCurrentSession, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-06 — happy path

> **Given** an account with an active session
> **When** they close the browser entirely and return to the link the next day
> **Then** the system still recognises them and shows them their own display name, without asking them to sign in
>
> — `spec.md §5, AC-06, verbatim` · full text: [spec.md](../spec.md)

### AC-08 — happy path

> **Given** an account with an active session
> **When** they sign out
> **Then** the system ends the session the sign-out travelled on — and only that one, leaving any session the same account holds on another device untouched — and presents them the view a visitor sees
>
> — `spec.md §5, AC-08, verbatim` · full text: [spec.md](../spec.md)

### AC-10 — authorization

> **Given** a visitor whose session has ended, by signing out or by expiry
> **When** they attempt to reach anything reserved for a signed-in account
> **Then** the system refuses and presents the sign-in form, regardless of what their browser still holds
>
> — `spec.md §5, AC-10, verbatim` · full text: [spec.md](../spec.md)

### AC-11 — happy path

> **Given** a visitor registering an account
> **When** they supply a display name
> **Then** the system records it and uses it, never the email address, wherever that account's actions are shown to anyone else, and no two accounts share a display name, so a name shown to other members identifies exactly one account
>
> — `spec.md §5, AC-11, verbatim` · full text: [spec.md](../spec.md)

This task delivers the client half: the display name is what the shell shows, and the sign-in form is what an unrecognised session gets.

## Checklist

- [ ] Typed API client for the four operations, generated from or hand-written against `openapi.yaml`, with credentials included on every call — `src/Uniqua.Projector.Web/src/api/accounts.ts`.
- [ ] `useSession()` — a TanStack Query hook over `GET /api/v1/accounts/me`, the single source of "who am I" for the whole client; no second copy of the account in another store.
- [ ] A `401` from any call invalidates the session query, so the client falls back to the sign-in form without a reload (AC-10).
- [ ] `AccountShell` — the four states: **loading** (while the bootstrap call is in flight), **visitor** (unrecognised → the sign-in form), **account** (recognised → the display name, never the email address), **error** (the call failed for a reason other than `401`, with a retry).
- [ ] Sign-out control calling `DELETE /api/v1/sessions/current`, then clearing the cached session so the visitor view appears immediately (AC-08).
- [ ] Composed from `Button`, `Card`, `Avatar`, `DropdownMenu` and Tailwind tokens only; no new primitive, no second styling mechanism.
- [ ] Component tests against a mocked transport for all four states and for the sign-out transition.

## Edge cases

| Case | Behaviour |
|---|---|
| The bootstrap call returns `401` | The sign-in form is shown; no error is surfaced — being a visitor is not a failure |
| The bootstrap call fails with a network error | The error state with a retry, distinct from the visitor state — a dead server must not look like being signed out |
| The session expires while the tab is open | The next call's `401` flips the client to the visitor view (AC-10) |
| Sign-out returns `401` (the session was already gone) | Treated as success: the end state is the same, the visitor view |
| Sign-out fails with a network error | The account view stays, with the failure surfaced — claiming a sign-out that did not happen is the one wrong answer here |
| The account's display name is empty or missing in the response | Impossible per the contract (`minLength: 1`, required); the client shows nothing rather than falling back to the email address (AC-11) |
| The bundle is still loading | The blank page sad §11 names; keep the bundle small and render the loading state as early as the framework allows |

## Definition of Done

- [ ] Component tests pass for all four shell states against a mocked transport.
- [ ] A test proves the display name is rendered and the email address is not, wherever the account is shown (AC-11).
- [ ] A test proves a `401` from any call lands the client on the sign-in form without a page reload (AC-10).
- [ ] A test proves the sign-out control leaves the client in the visitor state (AC-08).
- [ ] `grep` finds no reference to the session cookie's name or value anywhere in `src/Uniqua.Projector.Web/`.
- [ ] No component outside `components/ui/` is introduced, and no styling mechanism other than Tailwind utilities appears.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
