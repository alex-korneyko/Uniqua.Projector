---
id: T17
title: "Build the sign-in screen with one indistinguishable refusal"
layer: "ui"
deps: ["T15"]
blocks: []
acs: ["AC-04", "AC-05", "AC-05b"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/auth/SignInScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/__tests__/SignInScreen.test.tsx"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "todo"
---

# T17 — Build the sign-in screen with one indistinguishable refusal

## Place in the sequence

- **Blocked by:** T15 — the API client and session bootstrap · **Blocks:** — · **Wave:** 2, in parallel with the registration screen and the backend branch.
- **Lane:** own lane — only this task touches `SignInScreen.tsx`.

## Why (user story)

> **As a** visitor who already owns an account
> **I want** to sign in with the address and password I chose
> **So that** I get back to what is mine
>
> — `spec.md §4, US-02, verbatim` · full text: [spec.md](../spec.md)

This screen carries a path the build never exercises by accident: registration opens a session, so sign-in is only ever used by someone actually returning — which is why spec §7 measures it separately.

## Inlined context

**No `screens.md` exists**, so no `SCR-NN` id is cited. The four states are derived from the §5 acceptance criteria and the contract's single error response.

**States to build:** *default* · *submitting* (in flight, the button disabled) · *refused* (`401`, one message, whatever the cause) · *success* (hand over to the shell).

> `Spa-->>Visitor: Shows one message that names neither of the two` … `Spa-->>Visitor: Shows the same message, word for word`
>
> — `sad.md §6, flow 4, Spa branches, verbatim` · full text: [sad.md](../sad.md)

> `Spa-->>Guesser: Shows the same message, with no hint that a delay was applied`
>
> — `sad.md §6, flow 6, verbatim` · full text: [sad.md](../sad.md)

> The response names neither of the two and carries no `details`: any structured hint would be the enumeration oracle these criteria exist to close.
>
> — `contracts/openapi.yaml, operationId createSession, response 401, verbatim` · full text: [openapi.yaml](../contracts/openapi.yaml)

> The authentication screens reuse the vendored shadcn/ui primitives and Tailwind tokens already inventoried in `architecture-map.md` §Frontend, rather than introducing their own.
>
> — `adr/0007-deliver-the-web-surface-as-a-client-side-spa.md §Consequences, positive, verbatim` · full text: [adr/0007](../adr/0007-deliver-the-web-surface-as-a-client-side-spa.md)

**Components reused:** `Card`, `Input` (two fields), `Button` from the vendored shadcn/ui set, with the existing Tailwind tokens. **No new primitive** — this screen is deliberately the registration screen minus a field.

> **Returning sign-in succeeds on the first attempt** — baseline: 0 (the path does not exist), target: ≥ 90% over the first month. This metric exists because opening a session at registration means the sign-in path is never exercised during the build.
>
> — `spec.md §7, KPI 2, verbatim` · full text: [spec.md](../spec.md)

> **Hard rule:** Tailwind utility classes. This is the only styling mechanism in the repository; a second one is a review finding, not a preference.
>
> — `architecture-map.md §Frontend, verbatim` · full text: [architecture-map.md](../../../architecture-map.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [openapi.yaml](../contracts/openapi.yaml) · [sad.md](../sad.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

- `POST /api/v1/sessions` → `201` `Account` · `401 accounts.credentials_invalid`, `403 accounts.antiforgery_failed`.
- Request fields: `email` (≤ 256, **no** `format: email` — a client-side format rejection would be a second enumeration oracle), `password` (≤ 128, **no** `minLength: 8` — a short password must be refused as `credentials_invalid` like any other wrong one).
- One `401`, one wording: "The address or the password is incorrect." No `details`, and nothing in the response distinguishes a wrong password, an unregistered address, or a delayed attempt.

— `contracts/openapi.yaml, operationId createSession, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-04 — happy path

> **Given** a visitor who owns an account and has no active session
> **When** they submit the address and password they registered with
> **Then** the system opens a session and shows them their own display name
>
> — `spec.md §5, AC-04, verbatim` · full text: [spec.md](../spec.md)

### AC-05 — error

> **Given** a visitor who owns an account
> **When** they submit their address with the wrong password
> **Then** the system refuses and says only that the address or the password is incorrect, without revealing which of the two was wrong
>
> — `spec.md §5, AC-05, verbatim` · full text: [spec.md](../spec.md)

### AC-05b — error

> **Given** a visitor submitting the sign-in form with an address no account was ever registered with
> **When** they submit it with any password
> **Then** the system refuses with the same wording it uses for a wrong password and takes a comparable time to do so, so that neither the message nor the wait reveals whether the address is registered
>
> — `spec.md §5, AC-05b, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `SignInScreen` with address and password fields and a submit, composed from `Card`, `Input` and `Button` — `src/Uniqua.Projector.Web/src/features/auth/SignInScreen.tsx`.
- [ ] **No client-side validation of either field** beyond "not empty": no email-format check, no minimum password length. Both would be a second oracle the server is careful not to be.
- [ ] One refusal message for `401`, rendered identically whatever the cause, with both values left in place and focus returned to the password field.
- [ ] While a submission is in flight the button is disabled and no elapsed-time hint is shown — a 30-second wait must look like an ordinary one (AC-05b, AC-12).
- [ ] On `201`, invalidate the session query so the shell shows the display name (AC-04).
- [ ] A link across to the registration screen, and back, so a visitor who guessed wrong about owning an account is not stuck.
- [ ] Component tests against a mocked transport for all four states, including one that asserts the refusal text is identical for a wrong password and an unknown address.

## Edge cases

| Case | Behaviour |
|---|---|
| An address no account was registered with | The identical `401` message, with no client-side hint that the address was unknown (AC-05b) |
| A password of 3 characters | Submitted, and refused by the server as `credentials_invalid` — the screen must not pre-empt it (AC-05) |
| An address that is not a valid email shape | Submitted as typed; no format rejection client-side |
| A delayed attempt (30 s under AC-12) | The spinner runs longer; nothing else differs, and no timeout message appears before the server answers |
| An empty field | The only client-side refusal permitted: "fill in both fields", with no request sent |
| A double submit | The button is disabled while in flight |
| `403 accounts.antiforgery_failed` | A plain "please try again" with a fresh token, distinct from the credentials message |
| Sign-in while already signed in on another tab | The `201` simply opens a second session; the shell updates |

## Definition of Done

- [ ] Component tests pass for all four states against a mocked transport.
- [ ] A test asserts the refusal rendering is identical for a wrong password and for an unregistered address (AC-05, AC-05b).
- [ ] A test asserts no request-blocking format or length validation exists on either field — only the empty check.
- [ ] A test asserts a `201` lands the client in the signed-in shell showing the display name (AC-04).
- [ ] The screen introduces no component outside `components/ui/` and no second styling mechanism.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
