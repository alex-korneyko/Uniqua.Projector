---
id: T4
title: "Map the Session entity and generate the sessions-table migration with its three indexes"
layer: "migration"
deps: ["T1", "T2"]
blocks: ["T5", "T6", "T20"]
acs: ["AC-07", "AC-07b", "AC-08", "AC-10"]
files_hint:
  - "docs/features/accounts-and-sessions/migrations/02_create_sessions.up.sql"
  - "docs/features/accounts-and-sessions/migrations/02_create_sessions.down.sql"
  - "src/Uniqua.Projector.Infrastructure/AppDbContext.cs"
  - "src/Uniqua.Projector.Infrastructure/Accounts/SessionConfiguration.cs"
  - "src/Uniqua.Projector.Infrastructure/Migrations/"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "todo"
---

# T4 — Map the Session entity and generate the sessions-table migration with its three indexes

## Place in the sequence

- **Blocked by:** T1 — identity schema (the FK target), T2 — Session entity (the type being mapped) · **Blocks:** T5 — key-ring table, T6 — session store and ports, T20 — expired-session cleanup · **Wave:** 2.
- **Lane:** the migration lane, ordinal 02 of 03 — serialized behind T1 and ahead of T5. It also shares `AppDbContext.cs` with T1, which puts it in the same file lane.

## Why (user story)

> **As an** account
> **I want** my session to survive closing the browser
> **So that** returning to the link days later does not cost me a sign-in
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task gives the session somewhere to survive: a row that outlives the process, which is what makes a server-side session record possible at all (ADR 0008).

## Inlined context

> **Chosen:** Option 1 — server-side session records. A row per session (id, account, created, last-seen, revoked); the cookie carries an opaque reference and every authenticated request validates against the row. Option 1 pays one indexed read per authenticated request and gets all four session rules enforced in one observable place.
>
> — `adr/0008-store-sessions-as-server-side-records.md §Decision outcome, abridged` · full text: [adr/0008](../adr/0008-store-sessions-as-server-side-records.md)

> **Aggregate root:** root. It references the account but is not loaded through it — recognition reads a session by primary key and never materialises the account graph, which is the whole point of the ≤ 30 ms budget in spec §6.
>
> — `data-model.md §Entities, Sessions, verbatim` · full text: [data-model.md](../data-model.md)

> **Not indexed, deliberately:** `Sessions.Id` needs no secondary index — it is the primary key, and recognising a session is a primary-key lookup. `Sessions.LastSeenAt` is written on the hot path and never filtered on: the 14-day rule is evaluated on a row already fetched by primary key, so an index there would cost every write and serve no query.
>
> — `data-model.md §Indexes, verbatim` · full text: [data-model.md](../data-model.md)

> **Hard rule — promotion.** `implement` declares the model, runs `dotnet ef migrations add <Name>` in ordinal order, then runs `dotnet ef migrations script` and **diffs the generated SQL against the staged file**. A difference is a finding — either the model is wrong or this document is, and the two are reconciled before the migration is applied.
>
> — `data-model.md §Promotion, abridged` · full text: [data-model.md](../data-model.md)

> No `CHECK` on the ordering of the three timestamps: the 14-day and 90-day rules live on the `Session` entity in Domain, and restating them as database constraints would put one rule in two places that can disagree.
>
> — `data-model.md §Entities, Sessions constraints, verbatim` · full text: [data-model.md](../data-model.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([data-model.md](../data-model.md) · [sad.md](../sad.md) · [adr/0008](../adr/0008-store-sessions-as-server-side-records.md)) and follow it. Do not guess.

## Data delta

`Sessions` — new table, owned by this feature:

| Column | Type | Constraints | Change |
|---|---|---|---|
| `Id` | `uniqueidentifier` | PK, app-generated `Guid.CreateVersion7()` — **this value is the opaque reference the session cookie carries** | added |
| `AccountId` | `uniqueidentifier` | NOT NULL, FK → `AspNetUsers(Id)` ON DELETE CASCADE | added |
| `CreatedAt` | `datetimeoffset` | NOT NULL — the 90-day ceiling counts from here (AC-07b) | added |
| `LastSeenAt` | `datetimeoffset` | NOT NULL — the 14-day window counts from here (AC-07); written at most once an hour | added |
| `RevokedAt` | `datetimeoffset` | NULL — set on sign-out (AC-08); NULL means live; the row is kept, not deleted, so AC-10 is answered by a record rather than by an absence | added |

| Index | Columns | Unique | Query it serves |
|---|---|---|---|
| `IX_Sessions_AccountId` | `AccountId` | no | FK hygiene: the cascade from `AspNetUsers` finds a session's rows by this column |
| `IX_Sessions_CreatedAt` | `CreatedAt` | no | the cleanup sweep — sessions opened more than 90 days ago |
| `IX_Sessions_RevokedAt` | `RevokedAt`, filtered `WHERE RevokedAt IS NOT NULL` | no | the cleanup sweep — rows revoked more than 14 days ago; filtered so the index stays a fraction of the table and costs nothing when a session is opened |

**Staged migration pair:** [`migrations/02_create_sessions.up.sql`](../migrations/02_create_sessions.up.sql) · [`migrations/02_create_sessions.down.sql`](../migrations/02_create_sessions.down.sql)

— `data-model.md §Entities + §Indexes, Sessions, abridged` · full text: [data-model.md](../data-model.md)

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-07 — domain invariant

> **Given** an account whose session has carried no request for 14 days — any request made on the account's behalf counts as activity, including one that only reads
> **When** they return to the link
> **Then** the system no longer recognises them and presents the sign-in form, because a session ends after 14 days of inactivity
>
> — `spec.md §5, AC-07, verbatim` · full text: [spec.md](../spec.md)

### AC-07b — domain invariant

> **Given** an account whose session was opened 90 days ago and has been used steadily ever since
> **When** they return to the link
> **Then** the system no longer recognises them and presents the sign-in form, because no session outlives 90 days however actively it is used
>
> — `spec.md §5, AC-07b, verbatim` · full text: [spec.md](../spec.md)

### AC-08 — happy path

> **Given** an account with an active session
> **When** they sign out
> **Then** the system ends the session the sign-out travelled on — and only that one, leaving any session the same account holds on another device untouched — and presents them the view a visitor sees
>
> — `spec.md §5, AC-08, verbatim` · full text: [spec.md](../spec.md)

### AC-10 — authorization

> **Given** a visitor whose session has ended, by signing out or by expiry
> **When** they attempt to reach anything reserved for a signed-in account
> **Then** the system refuses and presents the sign-in form, regardless of what their browser still holds
>
> — `spec.md §5, AC-10, verbatim` · full text: [spec.md](../spec.md)

This task delivers the columns each of the four rules is evaluated against — `LastSeenAt`, `CreatedAt` and `RevokedAt`. The evaluation itself belongs to T2 and T12.

## Checklist

- [ ] `IEntityTypeConfiguration<Session>`: table `Sessions`, PK `Id`, the three timestamps, `RevokedAt` nullable — `src/Uniqua.Projector.Infrastructure/Accounts/SessionConfiguration.cs`.
- [ ] FK `AccountId` → `AspNetUsers(Id)` with `ON DELETE CASCADE`; no navigation property from the account to its sessions, so the account graph is never loaded through one.
- [ ] The three indexes, `IX_Sessions_RevokedAt` filtered `WHERE RevokedAt IS NOT NULL`.
- [ ] Register the configuration on `AppDbContext` — `src/Uniqua.Projector.Infrastructure/AppDbContext.cs`.
- [ ] `dotnet ef migrations add CreateSessions`, then `dotnet ef migrations script` and diff against the staged `02_*.up.sql`; reconcile any difference before applying.
- [ ] Test: deleting an account removes its sessions by cascade; no index exists on `LastSeenAt`.

## Edge cases

| Case | Behaviour |
|---|---|
| An account is deleted while holding live sessions | The cascade removes its session rows; `IX_Sessions_AccountId` keeps that from being a table scan. (No deletion path exists in the product — spec §3 — but the schema is correct if one ever does.) |
| A revoked session row is read again | The row is still there, with `RevokedAt` set; refusal comes from the record, not from an absence (AC-10) |
| Two sessions for the same account on two devices | Both rows are independent; ending one leaves the other untouched (AC-08) |
| A clock skew making `LastSeenAt` later than `now` | The schema permits it; no `CHECK` constrains timestamp ordering, deliberately — the rule lives on the entity |
| The generated SQL differs from the staged file | A finding: reconcile the model against `data-model.md`, or the document against the model, before applying |

## Definition of Done

- [ ] `dotnet ef migrations script` output has been diffed against `migrations/02_create_sessions.up.sql`, and any difference reconciled and recorded.
- [ ] The migration applies after 01 and reverts cleanly, matching the staged `.down.sql`.
- [ ] The three indexes exist with the filtered predicate on `IX_Sessions_RevokedAt`, and `LastSeenAt` carries no index.
- [ ] Session ids are produced by `Guid.CreateVersion7()`, never by the database.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
