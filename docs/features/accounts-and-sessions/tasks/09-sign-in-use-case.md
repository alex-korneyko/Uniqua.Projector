---
id: T9
title: "Implement the SignIn use case with the progressive delay and the indistinguishable refusal"
layer: "app"
deps: ["T3", "T6", "T7"]
blocks: ["T14", "T18"]
acs: ["AC-04", "AC-05", "AC-05b", "AC-12"]
files_hint:
  - "src/Uniqua.Projector.Application/Accounts/SignIn.cs"
  - "src/Uniqua.Projector.Application/ApplicationServiceCollectionExtensions.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SignInTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T9 — Implement the SignIn use case with the progressive delay and the indistinguishable refusal

## Place in the sequence

- **Blocked by:** T3 — the delay curve and errors, T6 — session ports, T7 — Identity account store · **Blocks:** T14 — sessions endpoints, T18 — quality-scenario tests · **Wave:** 4, in parallel with RegisterAccount and SignOut.
- **Lane:** own lane — nothing else touches `Application/Accounts/SignIn.cs`.

## Why (user story)

> **As a** visitor who already owns an account
> **I want** to sign in with the address and password I chose
> **So that** I get back to what is mine
>
> — `spec.md §4, US-02, verbatim` · full text: [spec.md](../spec.md)

This task delivers the return path, and the three refusals that must be impossible to tell apart.

## Inlined context

> `alt No account was ever registered with that address` → `App->>Infra: Verify the password against a dummy credential so the attempt costs the same time` → `App-->>Api: Refused - the address or the password is incorrect` → `else The account exists and the password is wrong` → `App->>Infra: Record one more consecutive failure against the account` → `else The account exists and the password is correct` → `App->>Infra: Return the consecutive-failure count to zero and open a session`
>
> — `sad.md §6, flow 4 «sign in on return», abridged` · full text: [sad.md](../sad.md)

> *Postcondition:* either a session exists and the failure count is zero, or nothing about the account changed except its failure count
>
> — `sad.md §6, flow 4 postcondition, verbatim` · full text: [sad.md](../sad.md)

> `App->>Domain: How long must this attempt be held back` → `Domain-->>App: A delay grown from the count - at least 2 seconds at the sixth failure, at least 30 seconds at the tenth` → `App->>Infra: Verify the password` → `App->>App: Hold the answer for the computed delay before replying`
>
> — `sad.md §6, flow 6 «the progressive delay under password guessing», abridged` · full text: [sad.md](../sad.md)

> The count returns to zero two ways, and the account never becomes unusable to its owner: […] `The owner supplies the correct password` → `App-->>Api: Accepted with no delay at all, and a session opened as in flow 4`; […] `Fifteen minutes pass in which no attempt is made at all` → the reset is derived from the last-attempt time on the next read, not kept alive by a timer - so a restart cannot lose it.
>
> — `sad.md §6, flow 6, abridged` · full text: [sad.md](../sad.md)

> | Latency p95, sign-in | ≤ 600 ms | server-side timing on the sign-in path, successful sign-ins only — attempts delayed by the guessing protection are excluded, since that delay is deliberate |
>
> — `spec.md §6, NFR row, abridged` · full text: [spec.md](../spec.md)

> **Hard rule — risk:** A 30-second guessing delay holds the request open, occupying a connection for its duration. Harmless at invited-reviewer scale; revisit if the §6 delay curve is ever raised.
>
> — `sad.md §11, risk row, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — errors:** A refusal carries exactly the plain-language reason its acceptance criterion specifies — no more (AC-05 must not reveal which of address or password was wrong).
>
> — `sad.md §8, Error handling, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [adr/0010](../adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md)) and follow it. Do not guess.

## Data delta

No schema change. This use case writes the two guessing-protection columns and, on success, one session row:

| Column | Type | Constraints | Change |
|---|---|---|---|
| `AspNetUsers.AccessFailedCount` | `int` | NOT NULL | incremented on failure, zeroed on success |
| `AspNetUsers.LastFailedAttemptAt` | `datetimeoffset` | NULL | written on every failure; read to derive the 15-minute reset |
| `Sessions.*` | — | — | one row opened on success |

— `data-model.md §Entities, tables AspNetUsers + Sessions, abridged` · full text: [data-model.md](../data-model.md)

## API contract

- `POST /api/v1/sessions` → `201` `Account` · errors: `401 accounts.credentials_invalid` for **all** of a wrong password, an unregistered address, and a delayed attempt.
- Request fields this task handles: `email` (deliberately **not** `format: email` — a format rejection would be a second enumeration oracle), `password` (deliberately **not** bounded by `minLength: 8` — a short password is refused as `credentials_invalid` like any other wrong one).
- The `401` body carries no `details`: any structured hint would be the oracle these criteria exist to close. The delay is not signalled in the response at all.

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

### AC-12 — error

> **Given** an account against which 5 consecutive sign-in attempts have already failed
> **When** a further attempt is made with a wrong password
> **Then** the system refuses it after a delay that grows with each additional failure, while a correct password is still accepted immediately, so that guessing becomes futile without the account ever becoming unusable to its owner; the count of consecutive failures returns to zero either on a correct password or after 15 minutes in which no attempt is made at all
>
> — `spec.md §5, AC-12, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `SignIn` use case taking address and password, returning either the account plus its opened session id, or the single `BadCredentials` error — `src/Uniqua.Projector.Application/Accounts/SignIn.cs`.
- [ ] Unknown-address branch: call `IAccountStore.VerifyDummyPassword`, then return `BadCredentials`. No counter is touched — there is no account to count against.
- [ ] Known-account branch: compute the delay with `GuessingDelay.For(...)` (T3) **before** verifying, so the cost of the verification is inside the same request.
- [ ] Verify first, then — on failure only — wait out the computed delay, then `RecordFailure`, then refuse. A correct password skips the delay entirely and calls `ResetFailures`.
- [ ] The delay is awaited asynchronously against `IClock`-compatible timing, so a test can drive it without sleeping in real time.
- [ ] Open the session through `ISessionStore.Open` on success only.
- [ ] Integration tests: the happy path; a wrong password; an unregistered address; the 6th and 10th failure floors; a correct password at 9 failures answered immediately; the 15-minute reset.

## Edge cases

| Case | Behaviour |
|---|---|
| An address that owns no account | Full dummy verification, then the identical refusal in a comparable time (AC-05b); nothing is written |
| An empty or malformed address at sign-in | Same refusal as a wrong password — the contract deliberately omits `format: email` here |
| A password shorter than 8 characters at sign-in | Refused as `credentials_invalid`, not as a validation error (AC-05) |
| A correct password with 9 stored failures | Accepted immediately, with no delay at all, and the count is zeroed — this is the assertion that catches a re-enabled lockout (sad §10 QG-1) |
| The 6th consecutive wrong password | Refused after ≥ 2 s; the response is byte-for-byte the refusal an undelayed attempt gets |
| 16 minutes since the last failure, count still 20 in the column | The effective count is zero, so the attempt is undelayed; the stored column is only reset on the next success |
| The instance restarts mid-guessing | Nothing is lost: the reset is derived from `LastFailedAttemptAt` on the next read, never from a timer |
| A delayed request is abandoned by the client | The delay is still observed server-side; abandoning it gains the guesser nothing |

## Definition of Done

- [ ] An integration test proves a successful sign-in opens exactly one session, returns the display name, and zeroes the failure count (AC-04).
- [ ] An integration test proves the wrong-password and unregistered-address refusals are identical in status, code and body, and comparable in elapsed time (AC-05, AC-05b).
- [ ] Integration tests against a controllable clock prove ≥ 2 s at the 6th consecutive failure, ≥ 30 s at the 10th, no delay on success, and the 15-minute reset (AC-12, spec §6).
- [ ] A test proves an account with many recent failures still accepts the correct password immediately.
- [ ] The delay curve is read from `GuessingDelay` and nowhere restated in this use case.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
