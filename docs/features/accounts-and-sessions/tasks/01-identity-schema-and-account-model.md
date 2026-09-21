---
id: T1
title: "Declare the Identity account model and generate the identity-schema migration"
layer: "migration"
deps: []
blocks: ["T4", "T7"]
acs: ["AC-03", "AC-11b", "AC-13"]
files_hint:
  - "docs/features/accounts-and-sessions/migrations/01_create_identity_schema.up.sql"
  - "docs/features/accounts-and-sessions/migrations/01_create_identity_schema.down.sql"
  - "src/Uniqua.Projector.Infrastructure/AppDbContext.cs"
  - "src/Uniqua.Projector.Infrastructure/Accounts/ProjectorUser.cs"
  - "src/Uniqua.Projector.Infrastructure/Migrations/"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T1 — Declare the Identity account model and generate the identity-schema migration

## Place in the sequence

- **Blocked by:** — (wave 1) · **Blocks:** T4 — Sessions table, T7 — Identity account store · **Wave:** 1, it is ordinal 01 of the three schemas and everything that persists an account waits on it.
- **Lane:** the migration lane — `implement` serializes every `layer: migration` task.

## Why (user story)

> **As a** visitor
> **I want** to create an account from the public link with no help from anyone
> **So that** I can reach the product on my own
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task builds the store that account can exist in, and the two unique indexes that make "one address, one account" and "one display name, one account" facts of the database rather than hopes of the code.

## Inlined context

> Table and column names are ASP.NET Core Identity's defaults, unchanged **[confirmed 2026-09-21 — the full default Identity schema is kept rather than trimmed to the tables this feature uses]**. The four columns at the bottom of the table are this feature's own additions to the Identity user.
>
> — `data-model.md §Entities, AspNetUsers, verbatim` · full text: [data-model.md](../data-model.md)

> Both `NormalizedEmail` and `NormalizedDisplayName` are produced **in the application**, by Identity's normalizer (trim, then invariant upper-case), and registration and sign-in call the identical code path. The database is not asked to make `A` equal `a`: no column collation is overridden, so restoring the database onto a server with a different default collation cannot silently change who counts as a duplicate.
>
> — `data-model.md §Entities, AspNetUsers normalisation, abridged` · full text: [data-model.md](../data-model.md)

> **Constraints:** UNIQUE on `NormalizedEmail`, `NormalizedDisplayName`, `NormalizedUserName`. No `CHECK` constraint, no trigger and no database `DEFAULT`: password length (AC-02), address shape (AC-02b) and the delay curve (AC-12) are refused before a row is ever written, and architecture-map §Conventions puts invariants in the Domain entity rather than in the store.
>
> — `data-model.md §Entities, AspNetUsers constraints, abridged` · full text: [data-model.md](../data-model.md)

> **Hard rule — IDs:** application-generated GUID version 7 (`Guid.CreateVersion7()`) — time-ordered, and it does not leak record counts.
>
> — `sad.md §2, Conventions, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — promotion.** `implement`, on each `layer: migration` task, does **not** copy these files anywhere. It: (1) declares the model; (2) runs `dotnet ef migrations add <Name>` in ordinal order — `CreateIdentitySchema`, `CreateSessions`, `CreateDataProtectionKeys`; (3) runs `dotnet ef migrations script` and **diffs the generated SQL against the staged file**. A difference is a finding — either the model is wrong or this document is, and the two are reconciled before the migration is applied.
>
> — `data-model.md §Promotion, abridged` · full text: [data-model.md](../data-model.md)

> **Hard rule — persistence:** EF Core only, behind repository ports declared in Application; no `DbContext` reaches Api or Domain.
>
> — `architecture-map.md §Conventions, persistence, verbatim` · full text: [architecture-map.md](../../../architecture-map.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

`AspNetUsers` — the full default Identity schema plus this feature's four additions:

| Column | Type | Constraints | Change |
|---|---|---|---|
| `Id` | `uniqueidentifier` | PK, app-generated `Guid.CreateVersion7()` | added |
| `NormalizedEmail` | `nvarchar(256)` | **UNIQUE** (`EmailIndex`, filtered `WHERE NormalizedEmail IS NOT NULL`) — Identity's default here is non-unique; making it unique is this feature's deliberate change | added |
| `DisplayName` | `nvarchar(50)` | NOT NULL | added |
| `NormalizedDisplayName` | `nvarchar(50)` | NOT NULL, **UNIQUE** (`DisplayNameIndex`) | added |
| `LastFailedAttemptAt` | `datetimeoffset` | NULL | added |
| `AccessFailedCount` | `int` | NOT NULL — Identity's counter, reused by ADR 0010 | added (Identity default) |
| `LockoutEnabled` / `LockoutEnd` | `bit` / `datetimeoffset` | Identity defaults; `LockoutEnabled` is set to `0` in **application configuration**, which the schema cannot enforce | added (Identity default, unused) |

Satellites `AspNetRoles`, `AspNetUserRoles`, `AspNetUserClaims`, `AspNetRoleClaims`, `AspNetUserLogins`, `AspNetUserTokens` are created at their Identity defaults and are expected to stay empty.

**Staged migration pair:** [`migrations/01_create_identity_schema.up.sql`](../migrations/01_create_identity_schema.up.sql) · [`migrations/01_create_identity_schema.down.sql`](../migrations/01_create_identity_schema.down.sql)

— `data-model.md §Entities + §Indexes, AspNetUsers, abridged` · full text: [data-model.md](../data-model.md)

## API contract

Internal — no API surface.

## Acceptance criteria

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

### AC-13 — cross-context

> **Given** a signed-in account acting anywhere in the product
> **When** a later feature records who acted
> **Then** the account offers exactly one stable identity to record, which this feature never reuses for a second account and never silently reassigns, so that ownership recorded against it stays meaningful for as long as the account exists
>
> — `spec.md §5, AC-13, verbatim` · full text: [spec.md](../spec.md)

This task delivers the physical half of all three — the two unique indexes and a `Guid.CreateVersion7()` primary key. The plain-language refusals are T8's and T13's half.

## Checklist

- [ ] Add the Identity + EF Core packages to `src/Uniqua.Projector.Infrastructure/` and the design-time tooling reference.
- [ ] `ProjectorUser : IdentityUser<Guid>` with `DisplayName`, `NormalizedDisplayName`, `LastFailedAttemptAt` — `src/Uniqua.Projector.Infrastructure/Accounts/ProjectorUser.cs`.
- [ ] `AppDbContext : IdentityDbContext<ProjectorUser, IdentityRole<Guid>, Guid>` — `src/Uniqua.Projector.Infrastructure/AppDbContext.cs`.
- [ ] Model configuration: `DisplayName` / `NormalizedDisplayName` as `nvarchar(50)` NOT NULL; `EmailIndex` made **unique** and filtered; `DisplayNameIndex` unique — same file.
- [ ] `AddInfrastructure(IServiceCollection)` extension registering `AppDbContext` and Identity — `src/Uniqua.Projector.Infrastructure/InfrastructureServiceCollectionExtensions.cs`.
- [ ] `dotnet ef migrations add CreateIdentitySchema`, then `dotnet ef migrations script` and diff against the staged `01_*.up.sql`; reconcile any difference before applying.
- [ ] Unit test over the model: both indexes are unique, `DisplayName` is required and capped at 50.

## Edge cases

| Case | Behaviour |
|---|---|
| `NormalizedEmail` is NULL (Identity permits it) | The unique index is filtered `WHERE NormalizedEmail IS NOT NULL`, so NULLs do not collide with each other |
| Two accounts differing only in letter case of the address | The application normalises before writing, so both produce the same `NormalizedEmail` and the second write is refused by `EmailIndex` |
| Two accounts differing only in case or surrounding whitespace of the display name | Same — trim + invariant upper-case into `NormalizedDisplayName`, refused by `DisplayNameIndex` |
| A restore onto a server with a different default collation | Nothing changes: no column collation is overridden, the comparison key is computed in the application |
| The generated SQL differs from the staged file | A finding, not a fix-up: reconcile model against `data-model.md`, or the document against the model, before applying |

## Definition of Done

- [ ] `dotnet ef migrations script` output has been diffed against `migrations/01_create_identity_schema.up.sql`, and any difference reconciled and recorded.
- [ ] The generated migration applies to an empty database and reverts cleanly (forward, then back to `0`), matching the staged `.down.sql`.
- [ ] Unique indexes exist on `NormalizedEmail` (filtered) and `NormalizedDisplayName`; in a test, a second insert colliding on either is refused by the database.
- [ ] Account ids are produced by `Guid.CreateVersion7()`, never by the database.
- [ ] No `DbContext` type is referenced from `Api` or `Domain`.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
