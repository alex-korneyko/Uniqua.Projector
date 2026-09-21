---
id: T5
title: "Generate the data-protection key-ring table so sessions survive a redeploy"
layer: "migration"
deps: ["T4"]
blocks: ["T12"]
acs: []
files_hint:
  - "docs/features/accounts-and-sessions/migrations/03_create_data_protection_keys.up.sql"
  - "docs/features/accounts-and-sessions/migrations/03_create_data_protection_keys.down.sql"
  - "src/Uniqua.Projector.Infrastructure/AppDbContext.cs"
  - "src/Uniqua.Projector.Infrastructure/Migrations/"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "todo"
---

# T5 — Generate the data-protection key-ring table so sessions survive a redeploy

## Place in the sequence

- **Blocked by:** T4 — sessions table (ordinal order only) · **Blocks:** T12 — session authentication handler, which is where the key ring is actually pointed at this table · **Wave:** 3.
- **Lane:** the migration lane, ordinal 03 of 03; also shares `AppDbContext.cs` with T1 and T4.

## Why (user story)

> **As an** account
> **I want** my session to survive closing the browser
> **So that** returning to the link days later does not cost me a sign-in
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task carries the part of that story no acceptance criterion states: a 14-day promise that a redeploy would otherwise quietly shorten to "until the next deploy".

**No §5 acceptance criterion covers this task** — `acs` is deliberately empty. It discharges a spec §6 NFR row and KPI 3 instead, both quoted below.

## Inlined context

> | Session survival across a redeploy | 100% of unexpired sessions survive a redeploy of the instance | post-deployment check; first verifiable at roadmap step 4, when a real deployment exists |
>
> — `spec.md §6, NFR row, verbatim` · full text: [spec.md](../spec.md)

> **Sessions surviving a redeploy** — baseline: 0 (unmeasured; the default behaviour is expected to be 0), target: 100%, checked after every deployment from week 2.
>
> — `spec.md §7, KPI 3, verbatim` · full text: [spec.md](../spec.md)

> **Chosen:** Option 1 — a table in the same database. Data Protection persists the key ring through EF Core into the store that already exists. Option 2 adds a second artefact to back up and a mount to remember — and a forgotten mount fails silently, restoring exactly the behaviour this decision exists to prevent.
>
> — `adr/0009-keep-the-data-protection-key-ring-in-the-database.md §Decision outcome, abridged` · full text: [adr/0009](../adr/0009-keep-the-data-protection-key-ring-in-the-database.md)

> Not a domain entity. The table shape is **fixed by `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore`** and cannot be chosen: the package reads and writes exactly these three columns.
>
> — `data-model.md §Entities, DataProtectionKeys, verbatim` · full text: [data-model.md](../data-model.md)

> `Id` | `int` | PK, `IDENTITY(1,1)` | **Deliberate divergence** from the `Guid.CreateVersion7()` convention — the framework owns this shape. Flagged in the audit report rather than diverged from silently.
>
> — `data-model.md §Entities, DataProtectionKeys, verbatim` · full text: [data-model.md](../data-model.md)

> **Hard rule:** The keys that protect sessions live in the same store as the accounts they protect, so one database compromise yields both. `Xml` is **enough at rest to mint a session for any account** — readable only by the identity the application runs as.
>
> — `sad.md §11 accepted debt + spec.md §6.1 «A forged session cookie», abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([data-model.md](../data-model.md) · [adr/0009](../adr/0009-keep-the-data-protection-key-ring-in-the-database.md)) and follow it. Do not guess.

## Data delta

`DataProtectionKeys` — new table, shape fixed by the framework package. It stands apart from the domain tables on purpose (ADR 0009):

| Column | Type | Constraints | Change |
|---|---|---|---|
| `Id` | `int` | PK, `IDENTITY(1,1)` — deliberate divergence from the Guid v7 convention | added |
| `FriendlyName` | `nvarchar(max)` | NULL | added |
| `Xml` | `nvarchar(max)` | NULL — the key material | added |

No index beyond the primary key, and none is justified: the table holds single-digit rows and is read in full at application start.

**Staged migration pair:** [`migrations/03_create_data_protection_keys.up.sql`](../migrations/03_create_data_protection_keys.up.sql) · [`migrations/03_create_data_protection_keys.down.sql`](../migrations/03_create_data_protection_keys.down.sql)

— `data-model.md §Entities + §Indexes, DataProtectionKeys, abridged` · full text: [data-model.md](../data-model.md)

## API contract

Internal — no API surface. The key material never appears in a request or a response, and this contract deliberately offers no way to read it.

## Acceptance criteria

None. This task carries no spec §5 acceptance criterion — the two commitments it discharges are the §6 NFR row and §7 KPI 3 quoted in *Inlined context*, and both are verified by a post-deployment check rather than by a Given/When/Then.

Its own testable outcome is in the Definition of Done: the key ring is read from and written to this table rather than regenerated per instance.

## Checklist

- [ ] Add `Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` to `src/Uniqua.Projector.Infrastructure/`.
- [ ] Implement `IDataProtectionKeyContext` on `AppDbContext` — `DbSet<DataProtectionKey> DataProtectionKeys` — `src/Uniqua.Projector.Infrastructure/AppDbContext.cs`.
- [ ] `dotnet ef migrations add CreateDataProtectionKeys`, then `dotnet ef migrations script` and diff against the staged `03_*.up.sql`; reconcile any difference before applying.
- [ ] Extend `AddInfrastructure` with `AddDataProtection().PersistKeysToDbContext<AppDbContext>()`, leaving the application name explicit so a rename cannot orphan the ring.
- [ ] Integration test: two application instances built over the same database share one key ring — a value protected by the first is unprotected by the second.

## Edge cases

| Case | Behaviour |
|---|---|
| The table is empty at first start | The framework creates the first key and writes it; nothing fails |
| The instance is replaced (container, host, or virtual machine) | The ring is read from the database, so every unexpired session is still recognised — the whole point of ADR 0009 |
| The application name changes between deploys | The ring is scoped by it and every session would break; the name is set explicitly so this cannot happen by accident |
| The database is restored from backup | The ring comes with it; there is no second artefact to remember (ADR 0009's positive consequence) |
| A copy of the database leaks | A full compromise, not a partial one — `Xml` can mint a session for any account. Recorded, not mitigated (spec §6.1, sad §11 accepted debt) |

## Definition of Done

- [ ] `dotnet ef migrations script` output has been diffed against `migrations/03_create_data_protection_keys.up.sql`, and any difference reconciled and recorded.
- [ ] The migration applies after 02 and reverts cleanly, matching the staged `.down.sql`.
- [ ] An integration test shows a payload protected by one application instance being unprotected by a second instance over the same database — the redeploy-survival property, tested without a deployment.
- [ ] The Data Protection application name is set explicitly rather than inherited from the content root.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
