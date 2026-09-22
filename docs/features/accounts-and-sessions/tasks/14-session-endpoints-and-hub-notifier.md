---
id: T14
title: "Expose the sign-in and sign-out endpoints and the Api-side revocation notifier"
layer: "ports"
deps: ["T9", "T10", "T11", "T12"]
blocks: ["T18", "T19"]
acs: ["AC-04", "AC-05", "AC-05b", "AC-08", "AC-09"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "src/Uniqua.Projector.Api/Accounts/HubSessionRevocationNotifier.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "done"
---

# T14 — Expose the sign-in and sign-out endpoints and the Api-side revocation notifier

## Place in the sequence

- **Blocked by:** T9 — SignIn, T10 — SignOut and the notifier port, T11 — ProblemDetails and antiforgery, T12 — the session cookie · **Blocks:** T18 — quality-scenario tests, T19 — the written session rules · **Wave:** 5.
- **Lane:** shares `AccountEndpoints.cs` with T13 — serialized in the endpoint lane; `implement` may close the pair together under one gate with both `SDD-Task` trailers.

## Why (user story)

> **As an** account
> **I want** signing out to end this browser's session across every channel at once
> **So that** leaving a shared machine does not leave the product open behind me
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

This task delivers the two ends of a session's life over HTTP — opened on sign-in, ended on sign-out — and the Api-side implementation of the port that carries the ending beyond HTTP.

## Inlined context

> `Api-->>Spa: Signed out and the cookie cleared` / `Spa-->>Account: Shows the view a visitor sees`
>
> — `sad.md §6, flow 2, verbatim` · full text: [sad.md](../sad.md)

> `SignOut` therefore depends on an Application-declared port, `ISessionRevocationNotifier`, whose implementation sits in Api next to the hub and is registered at startup.
>
> — `sad.md §5, Building block view, verbatim` · full text: [sad.md](../sad.md)

> `HubSessionRevocationNotifier.cs` — implements ISessionRevocationNotifier beside the hub
>
> — `sad.md §5, Api decomposition, verbatim` · full text: [sad.md](../sad.md)

> Two refusals are deliberately indistinguishable: a wrong password (AC-05) and an address no account was registered with (AC-05b) return the identical `accounts.credentials_invalid` code, the identical wording, and take a comparable time. AC-12's progressive delay is applied *before* this same refusal and is not signalled in the response at all: a delayed attempt is indistinguishable from an ordinary one, so nothing reveals that the account is under attack. A correct password is never delayed.
>
> — `contracts/openapi.yaml, operationId createSession description, verbatim` · full text: [openapi.yaml](../contracts/openapi.yaml)

> **Verification timing.** AC-09 and the §6 row "Session survival across a redeploy" cannot be exercised while this feature closes: the live-update channel arrives at roadmap step 8 and the first real deployment at step 4. Both are binding commitments stated here and verified at those steps.
>
> — `spec.md §5, verification timing note, abridged` · full text: [spec.md](../spec.md)

> **Hard rule — open question:** how signing out reaches an already-open live-update connection. Resolve before roadmap step 8. ADR 0008 makes it possible — the session is server-side state the hub can consult — but does not implement the notification; spec §8 question 1.
>
> — `sad.md §11, open question row, verbatim` · full text: [sad.md](../sad.md)

**Do not invent the hub.** No SignalR hub and no board exist at this feature's close. This task supplies a notifier that sits where the hub will be and records the revocation for it; the connection-dropping behaviour is built and verified at roadmap step 8, against the resolved open question.

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [openapi.yaml](../contracts/openapi.yaml)) and follow it. Do not guess.

## Data delta

No schema change. Sign-in opens one `Sessions` row and clears the two guessing-protection columns; sign-out writes `RevokedAt` on exactly one row. All of it goes through the use cases — this task touches no `DbContext`.

— `data-model.md §Entities, tables Sessions + AspNetUsers, abridged` · full text: [data-model.md](../data-model.md)

## API contract

- `POST /api/v1/sessions` → `201` `Account` + `Set-Cookie` · errors: `401 accounts.credentials_invalid`, `403 accounts.antiforgery_failed`. `security: []`.
  - Request fields: `email` (≤ 256, deliberately **not** `format: email`), `password` (≤ 128, deliberately **not** `minLength: 8`).
- `DELETE /api/v1/sessions/current` → `204` with the cookie expired (`Max-Age=0`) · errors: `401 accounts.session_not_recognised`, `403 accounts.antiforgery_failed`. Requires a recognised session.
- The `401` on sign-in carries no `details`; the `204` on sign-out carries no body.

— `contracts/openapi.yaml, operationIds createSession + deleteCurrentSession, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

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

### AC-08 — happy path

> **Given** an account with an active session
> **When** they sign out
> **Then** the system ends the session the sign-out travelled on — and only that one, leaving any session the same account holds on another device untouched — and presents them the view a visitor sees
>
> — `spec.md §5, AC-08, verbatim` · full text: [spec.md](../spec.md)

### AC-09 — cross-context

> **Given** an account that is signed in and is holding an open live-update connection to a board
> **When** they sign out
> **Then** the system stops delivering board updates over that already-open connection, because the end of a session withdraws access over every channel at once and not only over the one the sign-out travelled on
>
> — `spec.md §5, AC-09, verbatim` · full text: [spec.md](../spec.md)

AC-09's verification is deferred to roadmap step 8 (spec §5). What is due here is the notifier being in place, registered, and called on every revocation.

## Checklist

- [ ] `POST /api/v1/sessions` calling `SignIn` (T9), issuing the cookie on success, returning `201` with the account — `src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs`.
- [ ] `DELETE /api/v1/sessions/current` calling `SignOut` (T10) with the session id the request was recognised as, then clearing the cookie and returning `204`.
- [ ] Both routes require the antiforgery token (T11); sign-in is reachable with no session, sign-out requires one.
- [ ] `HubSessionRevocationNotifier` implementing `ISessionRevocationNotifier`, registered in place of T10's no-op — `src/Uniqua.Projector.Api/Accounts/HubSessionRevocationNotifier.cs`.
- [ ] Until the hub exists, the notifier records the revoked session id for the step-8 implementation and logs it structurally — with a comment naming spec §8 question 1 as the thing that completes it.
- [ ] The sign-in response carries no hint of a delay — the same status, headers and body whether the attempt waited 0 or 30 seconds.
- [ ] Integration tests: sign-in happy path and both refusals; sign-out clears the cookie, revokes exactly one session, and calls the notifier once.

## Edge cases

| Case | Behaviour |
|---|---|
| Sign-in while already holding a live session | A second session is opened; the first stays live. Sessions are per-browser, and nothing in the spec forbids two |
| Sign-out with no session | `401 accounts.session_not_recognised` — there is nothing to end |
| Sign-out with an already-revoked cookie | Same `401`: the handler refuses before the use case is reached (T12) |
| Sign-out of one device | The other device's session is untouched (AC-08); the notifier is told about exactly one session id |
| A delayed sign-in attempt (AC-12) | Identical response to an undelayed refusal — only later. No header, no field, no log line visible to the client says a delay happened |
| The notifier throws | The sign-out still succeeds and still returns `204`; the failure is logged (T10's rule) |
| A `204` with a body | Never — sign-out returns no content, per the contract |

## Definition of Done

- [ ] An integration test drives AC-04: `201`, the display name, and a cookie immediately accepted by `GET /api/v1/accounts/me`.
- [ ] An integration test asserts the AC-05 and AC-05b responses are identical in status, headers and body, and comparable in elapsed time.
- [ ] An integration test drives AC-08: `204`, the cookie cleared, the signed-out session refused afterwards, and a second session of the same account still live.
- [ ] An integration test asserts the notifier is called exactly once per sign-out with the revoked session id (the AC-09 obligation that is testable today).
- [ ] The response to a delayed sign-in attempt is byte-identical to an undelayed refusal.
- [ ] Neither endpoint builds an error body of its own, and neither touches a `DbContext`.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
