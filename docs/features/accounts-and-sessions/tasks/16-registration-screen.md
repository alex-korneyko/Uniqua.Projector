---
id: T16
title: "Build the registration screen with every refusal state the contract can return"
layer: "ui"
deps: ["T15"]
blocks: []
acs: ["AC-01", "AC-01b", "AC-02", "AC-02b", "AC-03", "AC-11b"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/auth/RegisterScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/__tests__/RegisterScreen.test.tsx"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T16 — Build the registration screen with every refusal state the contract can return

## Place in the sequence

- **Blocked by:** T15 — the API client and session bootstrap · **Blocks:** — · **Wave:** 2, in parallel with the sign-in screen and with the whole backend branch.
- **Lane:** own lane — `layer: ui` is not auto-serialized, and only this task touches `RegisterScreen.tsx`.

## Why (user story)

> **As a** visitor
> **I want** to create an account from the public link with no help from anyone
> **So that** I can reach the product on my own
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This screen *is* the feature's headline goal — the place where "no help from anyone" either happens or does not.

## Inlined context

**No `screens.md` exists**, so no `SCR-NN` id is cited. The six states below are derived from the §5 acceptance criteria and the contract's error responses, as ADR 0006 and the sad §11 risk row anticipated.

**States to build:** *default* · *validating* (a submission in flight, the form disabled) · *field-refused* (`400 password_invalid`, `400 email_invalid`) · *collision* (`409 email_taken`, `409 display_name_taken`) · *rate-limited* (`429`, with the time named) · *success* (the session is open; hand over to the shell, never to a sign-in form).

> `Spa-->>Visitor: Shows the reason and keeps what was typed` … `Spa-->>Visitor: Shows the reason, everything else stays in place`
>
> — `sad.md §6, flows 1 and 3, Spa branches, verbatim` · full text: [sad.md](../sad.md)

> Enumeration is deliberate here and only here: `accounts.email_taken` and `accounts.display_name_taken` name which field collided (spec §6.1 — an ambiguous form defeats unattended registration, which is load-bearing).
>
> — `contracts/openapi.yaml, operationId registerAccount description, verbatim` · full text: [openapi.yaml](../contracts/openapi.yaml)

> The authentication screens reuse the vendored shadcn/ui primitives and Tailwind tokens already inventoried in `architecture-map.md` §Frontend, rather than introducing their own.
>
> — `adr/0007-deliver-the-web-surface-as-a-client-side-spa.md §Consequences, positive, verbatim` · full text: [adr/0007](../adr/0007-deliver-the-web-surface-as-a-client-side-spa.md)

**Components reused:** `Card` (the form container), `Input` (three fields), `Button` (submit) from the vendored shadcn/ui set in `src/Uniqua.Projector.Web/src/components/ui/`, with Tailwind tokens from `index.css` / `tailwind.config.ts`. Validation messages are composed from `Input` plus text styled with the existing tokens — **no new primitive**, and no second styling mechanism. If a new primitive turns out to be unavoidable, stop and run `/sdd:design-system` rather than inventing one here.

> **Unattended registration completion** — target: every first-time visitor the owner actually hands the link to reaches a signed-in state with no intervention from him, within the first week the public link is live.
>
> — `spec.md §7, KPI 1, verbatim` · full text: [spec.md](../spec.md)

> **Hard rule:** Tailwind utility classes. This is the only styling mechanism in the repository; a second one (CSS modules, styled-components, inline style objects) is a review finding, not a preference.
>
> — `architecture-map.md §Frontend, verbatim` · full text: [architecture-map.md](../../../architecture-map.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [openapi.yaml](../contracts/openapi.yaml) · [architecture-map.md](../../../architecture-map.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

- `POST /api/v1/accounts` → `201` `Account` · `400 accounts.password_invalid`, `400 accounts.email_invalid`, `403 accounts.antiforgery_failed`, `409 accounts.email_taken`, `409 accounts.display_name_taken`, `429 accounts.registration_rate_limited`.
- Request fields this screen collects: `email` (≤ 256), `password` (8..128), `display_name` (1..50).
- The `429` carries `Retry-After` and `retry_after_seconds` — the screen must say *when* they may try again, not merely that they may not now.
- On `201` the session cookie is already set: go straight to the signed-in shell (T15).

— `contracts/openapi.yaml, operationId registerAccount, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-01 — happy path

> **Given** a visitor with no account, on the public link
> **When** they submit an unused email address, a password of at least 8 and at most 128 characters, and an unused display name of at most 50 characters
> **Then** the system creates the account, opens a session immediately without asking them to sign in again, and shows them their own display name
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-01b — error

> **Given** 5 accounts have already been registered from the same request source within the past minute
> **When** a further visitor from that same source submits the registration form
> **Then** the system refuses, says plainly that registration is temporarily limited, and tells them when they may try again
>
> — `spec.md §5, AC-01b, verbatim` · full text: [spec.md](../spec.md)

### AC-02 — error

> **Given** a visitor filling in the registration form
> **When** they submit a password shorter than 8 characters
> **Then** the system refuses to create the account and tells them, in plain language, that a password must be at least 8 characters long, leaving everything else they typed in place
>
> — `spec.md §5, AC-02, verbatim` · full text: [spec.md](../spec.md)

### AC-02b — error

> **Given** a visitor filling in the registration form
> **When** they submit something that cannot be an email address
> **Then** the system refuses to create the account, says plainly that the address is not usable, and leaves everything else they typed in place
>
> — `spec.md §5, AC-02b, verbatim` · full text: [spec.md](../spec.md)

### AC-03 — domain invariant

> **Given** an account already exists for a given email address
> **When** a visitor tries to register with that same address
> **Then** the system refuses and states plainly that the address is already registered, because an email address identifies exactly one account
>
> — `spec.md §5, AC-03, verbatim` · full text: [spec.md](../spec.md)

### AC-11b — domain invariant

> **Given** an account already uses a given display name
> **When** a visitor tries to register with that same display name
> **Then** the system refuses and states plainly that the name is taken, because a display name identifies exactly one account to the people who see it
>
> — `spec.md §5, AC-11b, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `RegisterScreen` with the three fields and a submit, composed from `Card`, `Input` and `Button` — `src/Uniqua.Projector.Web/src/features/auth/RegisterScreen.tsx`.
- [ ] Client-side hints matching the contract's bounds (8..128, ≤ 50, an address shape) — as help, never as the authority. The server's refusal is what the screen displays.
- [ ] On **every** refusal, the submitted values stay in the fields, the password included, and focus lands on the field named by the error (AC-02, AC-02b).
- [ ] Map each `accounts.*` code to its own message; the two `409`s say which field collided (deliberate, spec §6.1), and the `429` states the time from `retry_after_seconds`.
- [ ] An unmapped or unexpected status shows one plain fallback message and keeps the form intact — never a blank screen and never a raw code.
- [ ] On `201`, invalidate the session query so the shell picks up the signed-in state; do not route to the sign-in form (AC-01).
- [ ] Component tests against a mocked transport for each of the six states.

## Edge cases

| Case | Behaviour |
|---|---|
| A refusal arrives | Everything typed stays, the password too — a visitor who has to retype three fields is the failure KPI 1 measures |
| Both the address and the display name are taken | The one refusal the server returns is shown; the screen invents no second message |
| `429` with `retry_after_seconds: 37` | "Try again in 37 seconds" — the number is shown, not paraphrased away (AC-01b) |
| `403 accounts.antiforgery_failed` | A plain "please try again" and a fresh token, not a validation message on a field |
| A double submit | The button is disabled while in flight; the second click cannot create a second account |
| A password of exactly 8 characters | Accepted by the hints; the bound is inclusive |
| A 51-character display name | Refused client-side as a hint and, if submitted anyway, by the server |
| The network dies mid-submit | The form comes back intact with a retry; no claim is made about whether the account exists |

## Definition of Done

- [ ] Component tests pass for all six states against a mocked transport, each asserting the message the contract's example specifies.
- [ ] A test proves every field keeps its value after each of the four refusal codes (AC-02, AC-02b, AC-03, AC-11b).
- [ ] A test proves the `429` state names the wait in seconds (AC-01b).
- [ ] A test proves a `201` leaves the client signed in and never shows a sign-in form (AC-01).
- [ ] The screen introduces no component outside `components/ui/` and no styling mechanism other than Tailwind utilities.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
