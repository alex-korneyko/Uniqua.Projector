---
id: T3
title: "Build the Account entity, its sentinel errors and the progressive-delay rule"
layer: "domain"
deps: []
blocks: ["T7", "T8", "T9"]
acs: ["AC-02", "AC-02b", "AC-11", "AC-12"]
files_hint:
  - "src/Uniqua.Projector.Domain/Accounts/Account.cs"
  - "src/Uniqua.Projector.Domain/Accounts/AccountErrors.cs"
  - "src/Uniqua.Projector.Domain/Accounts/GuessingDelay.cs"
  - "tests/Uniqua.Projector.Domain.UnitTests/Accounts/"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "done"
---

# T3 — Build the Account entity, its sentinel errors and the progressive-delay rule

## Place in the sequence

- **Blocked by:** — (wave 1) · **Blocks:** T7 — Identity account store, T8 — RegisterAccount, T9 — SignIn · **Wave:** 1. Pure Domain, no dependencies; it starts alongside the first migration and the Session entity.
- **Lane:** own lane — nothing else touches `Domain/Accounts/Account.cs`.

## Why (user story)

> **As an** account
> **I want** repeated wrong-password attempts against me to become progressively futile
> **So that** a public sign-in form is not an open invitation
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

This task delivers the two rules the entity owns — what a well-formed account is, and how long an attempt against one must be held back — as pure functions that a clock can be pointed at.

## Inlined context

> **Hard rule — domain rules live in Domain:** an invariant such as "a card cannot move to a column of another board" is enforced by the entity, not by a use case or an endpoint. Without this rule the four-project split degrades into ceremony, which is the failure mode this foundation is most exposed to.
>
> — `architecture-map.md §Conventions, verbatim` · full text: [architecture-map.md](../../../architecture-map.md)

> `App->>Domain: How long must this attempt be held back` / `Domain-->>App: A delay grown from the count - at least 2 seconds at the sixth failure, at least 30 seconds at the tenth`
>
> — `sad.md §6, flow 6 «the progressive delay under password guessing», abridged` · full text: [sad.md](../sad.md)

> The reset is derived from the last-attempt time on the next read, not kept alive by a timer - so a restart cannot lose it.
>
> — `sad.md §6, flow 6 note, verbatim` · full text: [sad.md](../sad.md)

> | Progressive delay under guessing | 6th consecutive failure delayed ≥ 2 s; 10th ≥ 30 s; a correct password never delayed; the failure count returns to zero after 15 min with no attempt | integration test against a controllable clock |
>
> — `spec.md §6, NFR row, verbatim` · full text: [spec.md](../spec.md)

> **Chosen:** Option 1 — Identity's failure counter with a computed delay, lockout disabled. Option 1 changes only what is *done* with the count, not where the count lives.
>
> — `adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md §Decision outcome, abridged` · full text: [adr/0010](../adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md)

> `AccountErrors.cs` — sentinel errors: address taken, display name taken, bad credentials
>
> — `sad.md §5, internal decomposition, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — errors:** A refusal carries exactly the plain-language reason its acceptance criterion specifies — no more (AC-05 must not reveal which of address or password was wrong).
>
> — `sad.md §8, Error handling, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [adr/0010](../adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md)) and follow it. Do not guess.

## Data delta

No DB changes. The rule reads two columns T1 already creates — `AccessFailedCount` and `LastFailedAttemptAt` — but adds and alters nothing:

| Column | Type | Constraints | Change |
|---|---|---|---|
| `AccessFailedCount` | `int` | NOT NULL | read-only here |
| `LastFailedAttemptAt` | `datetimeoffset` | NULL | read-only here |

— `data-model.md §Entities, table AspNetUsers, abridged` · full text: [data-model.md](../data-model.md)

## API contract

Internal — no API surface. The refusals this task names are mapped to `accounts.*` problem codes by T11 and the endpoint tasks.

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

### AC-11 — happy path

> **Given** a visitor registering an account
> **When** they supply a display name
> **Then** the system records it and uses it, never the email address, wherever that account's actions are shown to anyone else, and no two accounts share a display name, so a name shown to other members identifies exactly one account
>
> — `spec.md §5, AC-11, verbatim` · full text: [spec.md](../spec.md)

### AC-12 — error

> **Given** an account against which 5 consecutive sign-in attempts have already failed
> **When** a further attempt is made with a wrong password
> **Then** the system refuses it after a delay that grows with each additional failure, while a correct password is still accepted immediately, so that guessing becomes futile without the account ever becoming unusable to its owner; the count of consecutive failures returns to zero either on a correct password or after 15 minutes in which no attempt is made at all
>
> — `spec.md §5, AC-12, verbatim` · full text: [spec.md](../spec.md)

This task owns the delay curve and the reset arithmetic; T9 is what actually waits, and T7 is what persists the count.

## Checklist

- [ ] `Account` entity — `Id`, `Email`, `DisplayName`, and a credential reference; `Id` allocated with `Guid.CreateVersion7()` — `src/Uniqua.Projector.Domain/Accounts/Account.cs`.
- [ ] Invariants on creation: password length 8..128 (AC-02), the address must be usable as an email address (AC-02b), display name 1..50 characters (AC-01 / AC-11). Each refusal returns its own sentinel error, never a bare boolean.
- [ ] `AccountErrors` — `EmailTaken`, `DisplayNameTaken`, `BadCredentials`, `PasswordInvalid`, `EmailInvalid`, `DisplayNameInvalid` — `src/Uniqua.Projector.Domain/Accounts/AccountErrors.cs`.
- [ ] `GuessingDelay.For(int consecutiveFailures, DateTimeOffset? lastFailedAttemptAt, DateTimeOffset now)` returning a `TimeSpan` — `src/Uniqua.Projector.Domain/Accounts/GuessingDelay.cs`.
- [ ] The curve: zero for the first 5 consecutive failures; ≥ 2 s at the 6th; ≥ 30 s at the 10th; growing monotonically in between. Pick a growth function and write the chosen shape in a comment beside it — the two named points are the contract, the curve between them is this task's choice.
- [ ] The reset: when `now - lastFailedAttemptAt >= 15 minutes`, the effective count is zero and the delay is zero — derived on read, never from a timer.
- [ ] Unit tests: the 6th and 10th failure floors; failure 5 is undelayed; the 15-minute reset boundary on both sides; the curve never decreases as the count grows.

## Edge cases

| Case | Behaviour |
|---|---|
| `AccessFailedCount` is 0 and `LastFailedAttemptAt` is NULL | Delay zero — the state of an account that has never failed |
| 40 consecutive failures | The delay keeps growing past the 10th floor; no lockout, ever — the owner's correct password is still accepted immediately (ADR 0010) |
| 20 failures, the last one 16 minutes ago | Delay zero: the count is treated as reset although the stored counter is still 20 |
| 20 failures, the last one exactly 15 minutes ago | Reset — the boundary is inclusive, matching «after 15 minutes in which no attempt is made» |
| A correct password against an account with 9 stored failures | No delay is computed at all; the caller never consults the curve on success (AC-12) |
| A password of exactly 8 or exactly 128 characters | Accepted; the bounds are inclusive (AC-01) |
| A display name of exactly 50 characters | Accepted; 51 is refused |

## Definition of Done

- [ ] Unit tests pass for both delay floors (≥ 2 s at the 6th failure, ≥ 30 s at the 10th), for the monotonic growth between them, and for the 15-minute reset on both sides of the boundary.
- [ ] Unit tests pass for each creation invariant, each returning its own sentinel error: password too short, password too long, unusable address, display name too long.
- [ ] No call to `DateTimeOffset.UtcNow` anywhere in `Domain` — every rule takes its instant as a parameter.
- [ ] `src/Uniqua.Projector.Domain/` still references no other project.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
