---
id: T8
title: "Implement the RegisterAccount use case: create the account and open its session in one step"
layer: "app"
deps: ["T3", "T6", "T7"]
blocks: ["T13"]
acs: ["AC-01", "AC-02", "AC-02b", "AC-03", "AC-11", "AC-11b", "AC-13"]
files_hint:
  - "src/Uniqua.Projector.Application/Accounts/RegisterAccount.cs"
  - "src/Uniqua.Projector.Application/ApplicationServiceCollectionExtensions.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RegisterAccountTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T8 — Implement the RegisterAccount use case: create the account and open its session in one step

## Place in the sequence

- **Blocked by:** T3 — Account entity and errors, T6 — session ports, T7 — Identity account store · **Blocks:** T13 — registration endpoint · **Wave:** 4, in parallel with SignIn and SignOut.
- **Lane:** own lane — nothing else touches `Application/Accounts/RegisterAccount.cs`.

## Why (user story)

> **As a** visitor
> **I want** to create an account from the public link with no help from anyone
> **So that** I can reach the product on my own
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task delivers the whole of that story on the server side: one call that creates the account and opens the session, so the visitor is never asked to sign in immediately after registering.

## Inlined context

> `App->>Infra: Is the address or the display name already taken` → `alt Address or display name already in use` → `App-->>Api: Refused, naming which one` → `else Both are free` → `App->>Domain: Build the account and check its invariants` → `App->>Infra: Store the account and open a session for it` → `App-->>Api: Account created and session opened`
>
> — `sad.md §6, flow 1 «register unaided and arrive signed in», abridged` · full text: [sad.md](../sad.md)

> *Postcondition:* nothing is written unless the submission survives every check above - no account, no session, no counter
>
> — `sad.md §6, flow 3 postcondition, verbatim` · full text: [sad.md](../sad.md)

> `RegisterAccount.cs` — use case: create + open a session in one step (AC-01)
>
> — `sad.md §5, Application decomposition, verbatim` · full text: [sad.md](../sad.md)

> *Account enumeration through the registration form*: accepted and deliberate. The form states plainly that an address is already registered (AC-03) and that a display name is taken (AC-11b), because an ambiguous form defeats unattended registration, which is load-bearing.
>
> — `spec.md §6.1, Abuse cases, abridged` · full text: [spec.md](../spec.md)

> **Hard rule — domain rules live in Domain:** an invariant is enforced by the entity, not by a use case or an endpoint.
>
> — `sad.md §2, Conventions, verbatim` · full text: [sad.md](../sad.md)

> | Latency p95, registration | ≤ 800 ms | server-side timing, sampled in the smoke run |
>
> — `spec.md §6, NFR row, verbatim` · full text: [spec.md](../spec.md)

> `Id` — This is the single stable identity AC-13 promises — allocated once, never reused, never reassigned.
>
> — `data-model.md §Entities, AspNetUsers, verbatim` · full text: [data-model.md](../data-model.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md)) and follow it. Do not guess.

## Data delta

No schema change. This use case performs the feature's only two-table write: one row in `AspNetUsers` and one in `Sessions`, both through the ports, and both or neither.

| Column | Type | Constraints | Change |
|---|---|---|---|
| `AspNetUsers.Id` | `uniqueidentifier` | PK | written once, `Guid.CreateVersion7()` (AC-13) |
| `AspNetUsers.NormalizedEmail` / `NormalizedDisplayName` | `nvarchar` | UNIQUE | probed, then written |
| `Sessions.*` | — | — | one row opened for the new account |

— `data-model.md §Entities, tables AspNetUsers + Sessions, abridged` · full text: [data-model.md](../data-model.md)

## API contract

- `POST /api/v1/accounts` → `201` `Account` · errors: `400 accounts.password_invalid`, `400 accounts.email_invalid`, `409 accounts.email_taken`, `409 accounts.display_name_taken`.
- Request fields this task handles: `email`, `password`, `display_name`.
- Response fields it supplies: `id`, `email`, `display_name`. Never `PasswordHash`, `AccessFailedCount` or `LastFailedAttemptAt`.
- The `429` rate limit and the `403` antiforgery refusal are **not** this task's — they are refused before the use case is reached (T13, T11).

— `contracts/openapi.yaml, operationId registerAccount, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-01 — happy path

> **Given** a visitor with no account, on the public link
> **When** they submit an unused email address, a password of at least 8 and at most 128 characters, and an unused display name of at most 50 characters
> **Then** the system creates the account, opens a session immediately without asking them to sign in again, and shows them their own display name
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

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

### AC-11 — happy path

> **Given** a visitor registering an account
> **When** they supply a display name
> **Then** the system records it and uses it, never the email address, wherever that account's actions are shown to anyone else, and no two accounts share a display name, so a name shown to other members identifies exactly one account
>
> — `spec.md §5, AC-11, verbatim` · full text: [spec.md](../spec.md)

### AC-11b — domain invariant

> **Given** an account already uses a given display name
> **When** a visitor tries to register with that same display name
> **Then** the system refuses and states plainly that the name is taken, because a display name identifies exactly one account to the people who see it
>
> — `spec.md §5, AC-11b, verbatim` · full text: [spec.md](../spec.md)

### AC-13 — cross-context

> **Given** a signed-in account acting anywhere in the product
> **When** a later feature records who acted
> **Then** the account offers exactly one stable identity to record, which this feature never reuses for a second account and never silently reassigns, so that ownership recorded against it stays meaningful for as long as the account exists
>
> — `spec.md §5, AC-13, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `RegisterAccount` use case taking address, password and display name and returning either the new account plus its opened session id, or one sentinel error — `src/Uniqua.Projector.Application/Accounts/RegisterAccount.cs`.
- [ ] Order of operations, matching flow 1 and flow 3's postcondition: validate through the `Account` entity (T3) **first**, then probe uniqueness, then write. Nothing is written before every check has passed.
- [ ] Probe both `NormalizedEmail` and `NormalizedDisplayName` and return the error naming **which** collided — the enumeration here is deliberate (spec §6.1).
- [ ] Open the session through `ISessionStore.Open` in the same unit of work, so AC-01's "without asking them to sign in again" cannot half-happen.
- [ ] Treat a unique-index violation from the database as the same refusal as the probe, so a concurrent duplicate is refused in the same words.
- [ ] `AddApplication(IServiceCollection)` registration — `src/Uniqua.Projector.Application/ApplicationServiceCollectionExtensions.cs`.
- [ ] Integration tests: the happy path leaves exactly one account and one live session; each refusal leaves zero of both.

## Edge cases

| Case | Behaviour |
|---|---|
| The address is free but the display name is taken | Refused with `DisplayNameTaken`; no account and no session are written |
| Both the address and the display name are taken | One refusal is returned; it names the address, so the visitor fixes the more fundamental collision first |
| Two visitors register the same address simultaneously | The probe passes for both, the unique index refuses the second write, and that refusal is translated to the same `EmailTaken` error (AC-03) |
| The password is exactly 8 or exactly 128 characters | Accepted — the bounds are inclusive |
| The password is 129 characters | Refused as `PasswordInvalid`, the same error AC-02 names, since the contract bounds the field at 128 |
| Opening the session fails after the account is written | The whole registration is rolled back; a created account with no session would break AC-01's "immediately" |
| The address differs only in case from an existing one | Refused — the normalised form collides (AC-03) |

## Definition of Done

- [ ] An integration test proves the happy path writes exactly one `AspNetUsers` row and one live `Sessions` row, and returns the display name (AC-01, AC-11).
- [ ] An integration test per refusal (AC-02, AC-02b, AC-03, AC-11b) proves the store is unchanged afterwards — no account, no session, no counter (flow 3 postcondition).
- [ ] A test proves a concurrent duplicate address is refused with the same error as a probed one.
- [ ] The returned account id is a `Guid.CreateVersion7()` value produced by the application (AC-13).
- [ ] No validation rule is restated in this use case that the `Account` entity already owns.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
