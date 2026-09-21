---
id: T7
title: "Put ASP.NET Core Identity behind IAccountStore, with lockout off and hashing tuned"
layer: "infra"
deps: ["T1", "T3"]
blocks: ["T8", "T9"]
acs: ["AC-05", "AC-05b", "AC-12"]
files_hint:
  - "src/Uniqua.Projector.Application/Accounts/Ports/IAccountStore.cs"
  - "src/Uniqua.Projector.Infrastructure/Accounts/IdentityAccountStore.cs"
  - "src/Uniqua.Projector.Infrastructure/InfrastructureServiceCollectionExtensions.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/IdentityAccountStoreTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T7 — Put ASP.NET Core Identity behind IAccountStore, with lockout off and hashing tuned

## Place in the sequence

- **Blocked by:** T1 — identity schema, T3 — Account entity and its errors · **Blocks:** T8 — RegisterAccount, T9 — SignIn · **Wave:** 2, alongside the sessions migration.
- **Lane:** own lane. It **declares** `IAccountStore` in Application and implements it here in the same change, per the contract-task rule.

## Why (user story)

> **As a** visitor who already owns an account
> **I want** to sign in with the address and password I chose
> **So that** I get back to what is mine
>
> — `spec.md §4, US-02, verbatim` · full text: [spec.md](../spec.md)

This task is where a password is verified and where the framework's own lockout is switched off, so that the delay curve T3 computes is the only thing standing between a guesser and the form.

## Inlined context

> **Chosen:** Option 1 — Identity's failure counter with a computed delay, lockout disabled: keep the counter the framework already maintains, switch off its lockout behaviour, and derive the delay from the count. **This supersedes the «lockout after repeated attempts» consequence recorded in ADR 0003.**
>
> — `adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md §Decision outcome, abridged` · full text: [adr/0010](../adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md)

> `LockoutEnabled` | `bit` | NOT NULL | Identity's default. **Set to `0` in application configuration**: ADR 0010 switched the framework's lockout off, and the schema cannot enforce that — only the QG-1 regression test can.
>
> — `data-model.md §Entities, AspNetUsers, verbatim` · full text: [data-model.md](../data-model.md)

> `App->>Infra: Verify the password against a dummy credential so the attempt costs the same time` / `Infra-->>App: Rejected`
>
> — `sad.md §6, flow 4 «sign in on return», first branch, verbatim` · full text: [sad.md](../sad.md)

> | Password verification cost | ≥ 100 ms per attempt | deliberately slow; guarded by a unit test over the hashing parameters |
>
> — `spec.md §6, NFR row, verbatim` · full text: [spec.md](../spec.md)

> **Normalisation.** Both `NormalizedEmail` and `NormalizedDisplayName` are produced in the application by Identity's normalizer (trim, then invariant upper-case), and registration and sign-in call the identical code path.
>
> — `data-model.md §Entities, AspNetUsers normalisation, abridged` · full text: [data-model.md](../data-model.md)

> **Hard rule — logging:** Structured, `module=accounts`. **Never log an email address on a sign-in failure**, and never log the cookie, the session reference or any credential — otherwise the log becomes the account-enumeration oracle that AC-05b exists to close.
>
> — `sad.md §8, Logging, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — risk:** The framework's account lockout could be re-enabled by someone who assumes it is the safe default, silently breaking AC-12. Mitigation: the QG-1 regression test asserts that an account with many recent failures still accepts the correct password immediately.
>
> — `sad.md §11, risk row, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([sad.md](../sad.md) · [data-model.md](../data-model.md) · [adr/0010](../adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md)) and follow it. Do not guess.

## Data delta

No schema change — T1 created these columns. This task reads and writes them through Identity:

| Column | Type | Constraints | Change |
|---|---|---|---|
| `NormalizedEmail` | `nvarchar(256)` | UNIQUE (`EmailIndex`) | read — the sign-in lookup key |
| `NormalizedDisplayName` | `nvarchar(50)` | NOT NULL, UNIQUE | read — the registration uniqueness probe |
| `PasswordHash` | `nvarchar(max)` | NULL per Identity's default | written on create, read on verify |
| `AccessFailedCount` | `int` | NOT NULL | incremented on failure, reset on success |
| `LastFailedAttemptAt` | `datetimeoffset` | NULL | written on every failure — the 15-minute reset is derived from it on the next read |
| `LockoutEnabled` | `bit` | NOT NULL | written `0`; the switch lives in configuration, not in the schema |

— `data-model.md §Entities, table AspNetUsers, abridged` · full text: [data-model.md](../data-model.md)

## API contract

Internal — no API surface. The refusal this task produces is mapped to exactly one problem code by T14:

- `accounts.credentials_invalid` — one code and one wording for a wrong password, an unregistered address, and a delayed attempt alike.

— `contracts/openapi.yaml, operationId createSession, response 401, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

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

This task delivers the comparable cost (the dummy verification), the persisted counter and last-attempt time, and the absence of lockout. The waiting itself is T9's.

## Checklist

- [ ] `IAccountStore` — `FindByEmail`, `Create`, `VerifyPassword`, `VerifyDummyPassword`, `RecordFailure`, `ResetFailures`, `IsDisplayNameTaken` — `src/Uniqua.Projector.Application/Accounts/Ports/IAccountStore.cs`. The port speaks in Domain terms; no Identity type crosses it.
- [ ] `IdentityAccountStore` over `UserManager<ProjectorUser>` — `src/Uniqua.Projector.Infrastructure/Accounts/IdentityAccountStore.cs`.
- [ ] `FindByEmail` normalises through Identity's own normalizer, so registration and sign-in share one code path.
- [ ] `VerifyDummyPassword` hashes against a fixed dummy hash computed at startup, so an unregistered address costs a full verification (AC-05b).
- [ ] `RecordFailure` increments `AccessFailedCount` **and** writes `LastFailedAttemptAt` from `IClock`; `ResetFailures` zeroes both.
- [ ] Identity options: `Lockout.AllowedForNewUsers = false`, and password-policy checks left to the Domain entity rather than duplicated in Identity's validators.
- [ ] Hasher parameters tuned so one verification costs ≥ 100 ms on the §6 reference machine, with the chosen iteration count named in a constant beside a unit test that asserts it.
- [ ] Every log line in this file is `module=accounts` and carries no address, no credential and no session reference.

## Edge cases

| Case | Behaviour |
|---|---|
| The address belongs to no account | `FindByEmail` returns null and the caller still pays `VerifyDummyPassword` — same wording, comparable time (AC-05b) |
| The account has no `PasswordHash` (possible per Identity's nullable column) | Treated as a failed verification, never as a success |
| `AccessFailedCount` reaches Identity's own lockout threshold | Nothing happens — lockout is off; the count is data for the delay curve only (ADR 0010) |
| Two concurrent failures against one account | Both increment; a lost update understates the count by one, which delays slightly less. Accepted — the curve is a floor, not an exact ledger |
| A correct password after many failures | `ResetFailures` zeroes the count and the last-attempt time, and the verification is never delayed (the QG-1 regression assertion) |
| The address differs only in letter case or surrounding whitespace from the registered one | Found — the normalizer trims and upper-cases before the lookup |
| A sign-in failure is logged | The line names no address; otherwise the log itself becomes the enumeration oracle AC-05b closes |

## Definition of Done

- [ ] A unit test over the hashing parameters asserts the configured iteration count meets the ≥ 100 ms floor of spec §6, and fails if someone lowers it.
- [ ] An integration test shows `VerifyDummyPassword` taking a time comparable to a real verification for an address that owns no account.
- [ ] An integration test asserts `Lockout.AllowedForNewUsers` is false and that an account with 20 recent failures still verifies its correct password immediately (the sad §11 regression).
- [ ] `RecordFailure` persists both the count and `LastFailedAttemptAt`; `ResetFailures` clears both.
- [ ] No Identity type appears in any signature on `IAccountStore`, and no log line in this task carries an address or a credential.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
