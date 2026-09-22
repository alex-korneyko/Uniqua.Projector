---
id: T11
title: "Set up the single ProblemDetails handler and the antiforgery requirement"
layer: "wiring"
deps: []
blocks: ["T12", "T13", "T14"]
acs: ["AC-02", "AC-02b", "AC-05"]
files_hint:
  - "src/Uniqua.Projector.Api/ProblemDetailsSetup.cs"
  - "src/Uniqua.Projector.Api/Antiforgery/AntiforgerySetup.cs"
  - "src/Uniqua.Projector.Api/Program.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/ProblemDetailsTests.cs"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "done"
---

# T11 — Set up the single ProblemDetails handler and the antiforgery requirement

## Place in the sequence

- **Blocked by:** — (wave 1) · **Blocks:** T12 — session authentication handler, T13 — registration endpoint, T14 — sessions endpoints · **Wave:** 1. Every endpoint task needs the one error shape and the forgery guard to already exist, so this lands before any of them.
- **Lane:** shares `src/Uniqua.Projector.Api/Program.cs` with T12, T13 and T20 — serialized, and in dependency order anyway.

## Why (user story)

> **As a** visitor
> **I want** to create an account from the public link with no help from anyone
> **So that** I can reach the product on my own
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

Unattended registration depends on a refusal a stranger can act on. This task delivers the one place refusals are shaped — and the guarantee that a refusal says exactly what its acceptance criterion allows and no more.

## Inlined context

> **Hard rule — error handling:** every failure response is an RFC 9457 `ProblemDetails` from one exception handler; endpoints never build an ad-hoc error shape. A refusal carries exactly the plain-language reason its acceptance criterion specifies — no more (AC-05 must not reveal which of address or password was wrong).
>
> — `sad.md §8, Error handling, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — cross-site request forgery:** An antiforgery token is required on every state-changing request. The cookie alone is never sufficient proof of intent, because the browser attaches it automatically.
>
> — `sad.md §8, Cross-site request forgery, verbatim` · full text: [sad.md](../sad.md)

> *A state-changing request forged by another site*: because the session is carried by a cookie the browser attaches on its own, a page on another site could otherwise act as a signed-in account; state-changing requests are accepted only with proof that they originated from this application.
>
> — `spec.md §6.1, Abuse cases, verbatim` · full text: [spec.md](../spec.md)

> Cross-site request-forgery protection must be configured deliberately on state-changing endpoints; getting it wrong is the failure ADR 0003 already flagged as easy to get subtly wrong. §8 carries the row and §10 carries the verification.
>
> — `adr/0007-deliver-the-web-surface-as-a-client-side-spa.md §Consequences, negative, verbatim` · full text: [adr/0007](../adr/0007-deliver-the-web-surface-as-a-client-side-spa.md)

> **Open question carried into this task — OQ-API-1.** `sad.md` §8 requires an antiforgery token on every state-changing request, and the contract declares `X-XSRF-TOKEN` required on `registerAccount`, `createSession` and `deleteCurrentSession`. But **no §6 flow and no §5 AC shows how the client obtains one**, and `accounts.antiforgery_failed` is consequently the only error code in the contract with no acceptance criterion behind it. Owner: `sequences`. Due: before the contract is finalized.
>
> — `contracts/api-sync-report.md §Open questions, OQ-API-1, abridged` · full text: [api-sync-report.md](../contracts/api-sync-report.md)

**Do not invent the handshake.** Implement the standard token-issuing path the framework offers and record in the code comment which shape was chosen, then say so in the handoff so `sequences` can draw it and `/sdd:api --reconcile` can bind it. If the chosen shape would change an endpoint's contract, stop and raise it rather than editing `openapi.yaml` here.

**Fallback:** insufficient or contradicted by the code → read the named file in full ([sad.md](../sad.md) · [openapi.yaml](../contracts/openapi.yaml) · [api-sync-report.md](../contracts/api-sync-report.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

This task owns the shape of every error body in the feature, and two of its responses:

- `Problem` schema — required `type`, `title`, `status`, `code`; optional `detail`, `instance`, `retry_after_seconds`. `code` matches `^[a-z_]+\.[a-z_]+$`.
- `403 accounts.antiforgery_failed` — "The request could not be verified as coming from this application." The session, if any, is untouched.
- `X-XSRF-TOKEN` request header, required on every state-changing operation, `maxLength: 512`.
- The eight `accounts.*` codes the endpoints use are produced through this handler and nowhere else: `password_invalid`, `email_invalid`, `email_taken`, `display_name_taken`, `registration_rate_limited`, `credentials_invalid`, `session_not_recognised`, `antiforgery_failed`.

— `contracts/openapi.yaml, components.schemas.Problem + components.parameters.AntiforgeryToken + responses.AntiforgeryFailed, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

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

### AC-05 — error

> **Given** a visitor who owns an account
> **When** they submit their address with the wrong password
> **Then** the system refuses and says only that the address or the password is incorrect, without revealing which of the two was wrong
>
> — `spec.md §5, AC-05, verbatim` · full text: [spec.md](../spec.md)

This task co-owns the *plain language* and *and no more* halves of these three: the wording table and the guarantee that nothing else leaks into a body. The endpoints (T13, T14) own which code is returned when.

## Checklist

- [ ] One exception handler mapping each `AccountErrors` sentinel to its `accounts.*` code, status and wording — `src/Uniqua.Projector.Api/ProblemDetailsSetup.cs`.
- [ ] The wording table lives in this one file, so a reviewer can read every refusal the feature can produce in one place.
- [ ] `detail` is a fixed string per code — never a framework message, never an exception message, never an echo of submitted input.
- [ ] `code` is emitted as an RFC 9457 extension member and matches the contract's pattern.
- [ ] Antiforgery configured and required on state-changing endpoints; a missing or mismatched token produces `403 accounts.antiforgery_failed` through the same handler — `src/Uniqua.Projector.Api/Antiforgery/AntiforgerySetup.cs`.
- [ ] Record the chosen token-acquisition shape in a comment naming OQ-API-1, so the handoff can point `sequences` at it.
- [ ] Wire both into `Program.cs`, and add a test that an unmapped exception becomes a bare `500` problem carrying no exception text.

## Edge cases

| Case | Behaviour |
|---|---|
| An unmapped exception escapes a use case | `500` with a generic problem body; no stack trace, no message, no type name in the response |
| Two different failures map to one code (AC-05 / AC-05b / AC-12) | All three produce byte-identical bodies — the handler is given one sentinel and cannot tell them apart |
| A validation failure carries the submitted value | Never echoed into `detail`; the client keeps what was typed (AC-02, AC-02b) because it never cleared it, not because the server returns it |
| The antiforgery token is absent on a `GET` | Not required — only state-changing requests are guarded |
| The antiforgery token is present but stale | `403 accounts.antiforgery_failed`; the session is untouched, so the account is still signed in |
| A refusal is logged | `module=accounts`, with no address, no credential and no session reference (sad §8) |

## Definition of Done

- [ ] An integration test asserts every one of the eight `accounts.*` codes is produced with the status, `title` and `detail` the contract's examples state.
- [ ] An integration test asserts the AC-05 and AC-05b bodies are byte-identical.
- [ ] An integration test asserts a state-changing request without the token is refused `403 accounts.antiforgery_failed`, and that its session remains live afterwards.
- [ ] An integration test asserts an unmapped exception yields a problem body containing no exception message and no stack trace.
- [ ] No endpoint anywhere in the feature constructs an error body of its own.
- [ ] The chosen antiforgery-token acquisition shape is recorded in the code against OQ-API-1 and reported in the handoff; `openapi.yaml` is left unedited.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
