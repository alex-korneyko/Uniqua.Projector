---
id: T6
title: "Declare the session ports and implement them over EF Core"
layer: "infra"
deps: ["T2", "T4"]
blocks: ["T8", "T9", "T10", "T12", "T20"]
acs: ["AC-06", "AC-08", "AC-10"]
files_hint:
  - "src/Uniqua.Projector.Application/Accounts/Ports/ISessionStore.cs"
  - "src/Uniqua.Projector.Application/Accounts/Ports/ISessionReader.cs"
  - "src/Uniqua.Projector.Application/Accounts/Ports/IClock.cs"
  - "src/Uniqua.Projector.Infrastructure/Accounts/SessionStore.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionStoreTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "done"
---

# T6 — Declare the session ports and implement them over EF Core

## Place in the sequence

- **Blocked by:** T2 — Session entity, T4 — sessions table · **Blocks:** T8 — RegisterAccount, T9 — SignIn, T10 — SignOut, T12 — session authentication handler, T20 — expired-session cleanup · **Wave:** 3. It is the busiest node in the graph: five tasks wait on it.
- **Lane:** own lane. It **declares** `ISessionStore`, `ISessionReader` and `IClock` in Application and implements them in Infrastructure in the same change — per the contract-task rule, a shared interface is never committed without its first implementation.

## Why (user story)

> **As an** account
> **I want** my session to survive closing the browser
> **So that** returning to the link days later does not cost me a sign-in
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task is the only code allowed to touch a session row, and the reason the session rules are enforced in one observable place rather than in four.

## Inlined context

> The one placement that is not simply inherited is **session recognition**. It runs on every authenticated request, so it belongs in the request pipeline in `Api` — but it must read a session record, which only `Infrastructure` may do. It is therefore an Api-level authentication handler that calls an **Application port** (`ISessionReader`), implemented in Infrastructure.
>
> — `sad.md §5, Building block view, verbatim` · full text: [sad.md](../sad.md)

> `Ports/` — ISessionStore, ISessionReader, IAccountStore, IClock, ISessionRevocationNotifier - declared here, implemented in Api
>
> — `sad.md §5, Application decomposition, verbatim` · full text: [sad.md](../sad.md)

> - Recognise a session on an ordinary request → **primary-key lookup, no secondary index**.
> - Sweep rows opened more than 90 days ago → `IX_Sessions_CreatedAt`.
> - Sweep rows revoked more than 14 days ago → `IX_Sessions_RevokedAt` (filtered).
>
> — `data-model.md §Entities, Sessions access patterns, abridged` · full text: [data-model.md](../data-model.md)

> `Api->>Infra: Stamp this request as activity on the session` / *note:* persists last-seen-at on the session - written at most once an hour, which is the 1 hour of slack spec section 6 allows on expiry accuracy
>
> — `sad.md §6, flow 5, abridged` · full text: [sad.md](../sad.md)

> `RevokedAt` — Set when the account signs out (AC-08). NULL means live. A revoked row is kept, not deleted, so that AC-10 («refuses regardless of what their browser still holds») is answered by a record rather than by an absence; the sweep removes it 14 days later.
>
> — `data-model.md §Entities, Sessions, verbatim` · full text: [data-model.md](../data-model.md)

> | Latency p95, recognising a session on an ordinary read | ≤ 30 ms | server-side timing on the sign-in path […] sampled in the smoke run |
>
> — `spec.md §6, NFR row, abridged` · full text: [spec.md](../spec.md)

> **Hard rule — persistence:** EF Core only, behind repository ports declared in Application; no `DbContext` reaches Api or Domain.
>
> — `architecture-map.md §Conventions, verbatim` · full text: [architecture-map.md](../../../architecture-map.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([sad.md](../sad.md) · [data-model.md](../data-model.md) · [adr/0008](../adr/0008-store-sessions-as-server-side-records.md)) and follow it. Do not guess.

## Data delta

No schema change — T4 created the table. This task reads and writes those columns:

| Column | Type | Constraints | Change |
|---|---|---|---|
| `Id` | `uniqueidentifier` | PK | read (the only lookup key on the recognition path) |
| `AccountId` | `uniqueidentifier` | NOT NULL, FK | written on open |
| `CreatedAt` | `datetimeoffset` | NOT NULL | written on open |
| `LastSeenAt` | `datetimeoffset` | NOT NULL | written at most once an hour |
| `RevokedAt` | `datetimeoffset` | NULL | written on sign-out |

— `data-model.md §Entities, table Sessions, abridged` · full text: [data-model.md](../data-model.md)

## API contract

Internal — no API surface. The session id these ports return is the opaque value the cookie carries; it never appears in a request or response body.

— `contracts/openapi.yaml, components.securitySchemes.SessionCookie, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-06 — happy path

> **Given** an account with an active session
> **When** they close the browser entirely and return to the link the next day
> **Then** the system still recognises them and shows them their own display name, without asking them to sign in
>
> — `spec.md §5, AC-06, verbatim` · full text: [spec.md](../spec.md)

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

This task delivers the read that recognises a live session, the revoke that ends exactly one, and the kept row that makes the refusal come from a record rather than from an absence.

## Checklist

- [ ] `IClock` with `DateTimeOffset Now` — `src/Uniqua.Projector.Application/Accounts/Ports/IClock.cs`; a system implementation in Infrastructure, and nothing anywhere else calls `DateTimeOffset.UtcNow`.
- [ ] `ISessionReader.Find(Guid sessionId)` returning the `Session` or null — `Application/Accounts/Ports/ISessionReader.cs`. One port, one method: this is the ≤ 30 ms path.
- [ ] `ISessionStore` — `Open(Guid accountId)`, `Revoke(Guid sessionId)`, `StampActivity(Guid sessionId)`, `DeleteExpired()` — `Application/Accounts/Ports/ISessionStore.cs`.
- [ ] `SessionStore` implementing both over `AppDbContext` — `src/Uniqua.Projector.Infrastructure/Accounts/SessionStore.cs`. `Find` is a primary-key lookup, no `Include`, no projection of the account.
- [ ] `StampActivity` writes only when `Session.ShouldStampActivity(now)` is true (T2), so an ordinary read usually writes nothing.
- [ ] `DeleteExpired()` removes rows opened more than 90 days ago or revoked more than 14 days ago — the T20 sweep's query, living here because only Infrastructure may issue it.
- [ ] Register all three in `AddInfrastructure` — `src/Uniqua.Projector.Infrastructure/InfrastructureServiceCollectionExtensions.cs`.
- [ ] Integration tests against the SQL Server container for open, find, revoke, the once-an-hour stamp, and the sweep query.

## Edge cases

| Case | Behaviour |
|---|---|
| `Find` is given an id that was never issued | Returns null; the caller refuses without distinguishing "unknown" from "revoked" (AC-10) |
| `Find` is given a revoked session | Returns the row with `RevokedAt` set — the refusal is a record, not an absence |
| `Revoke` is called twice on the same session | Idempotent: `RevokedAt` keeps its first value, so the audit of when it ended stays true |
| `Revoke` is called on a session of another account | Not this port's concern — it revokes exactly the id it is given; the caller (T10) only ever passes the id the request arrived on (AC-08) |
| `StampActivity` inside the one-hour window | No write at all; the row is left alone |
| Two concurrent requests on one session both want to stamp | Last write wins; both are within the same hour, so the value is equivalent |
| `DeleteExpired` finds nothing | Returns zero; the sweep is hygiene and a no-op run is a success (T20) |

## Definition of Done

- [ ] Integration tests pass for opening, finding, revoking and sweeping, against the SQL Server container through the real `AppDbContext`.
- [ ] A test proves `Find` issues exactly one primary-key query and materialises no account row.
- [ ] A test proves `StampActivity` writes nothing when the last stamp is under an hour old, and writes once when it is older.
- [ ] Revoking one of two sessions held by the same account leaves the other live (AC-08).
- [ ] `IClock` is the only source of time in Application and Infrastructure for this feature.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
