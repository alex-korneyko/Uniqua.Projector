---
id: T2
title: "Build the Session entity owning the 14-day idle and 90-day absolute expiry rules"
layer: "domain"
deps: []
blocks: ["T4", "T6", "T12"]
acs: ["AC-07", "AC-07b"]
files_hint:
  - "src/Uniqua.Projector.Domain/Accounts/Session.cs"
  - "tests/Uniqua.Projector.Domain.UnitTests/Accounts/SessionTests.cs"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "done"
---

# T2 — Build the Session entity owning the 14-day idle and 90-day absolute expiry rules

## Place in the sequence

- **Blocked by:** — (wave 1) · **Blocks:** T4 — Sessions table, T6 — session store and ports, T12 — session authentication handler · **Wave:** 1. It is pure Domain with no dependencies, so it starts together with the first migration.
- **Lane:** own lane — nothing else touches `Domain/Accounts/Session.cs`.

## Why (user story)

> **As an** account
> **I want** my session to survive closing the browser
> **So that** returning to the link days later does not cost me a sign-in
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

This task delivers the one object that decides when that survival ends — and nothing else in the feature is allowed to re-derive that arithmetic.

## Inlined context

> The rule that a session is expired — 14 days idle or 90 days old — lives on the **Session entity in Domain**, not in the handler, so that the handler asks the entity rather than re-deriving the arithmetic.
>
> — `sad.md §5, Building block view, verbatim` · full text: [sad.md](../sad.md)

> `Api->>Domain: Is this session expired as of now` / `Domain-->>Domain: Apply the two rules the Session entity owns - 14 days without a request, and 90 days since it was opened` / `Domain-->>Api: Verdict`
>
> — `sad.md §6, flow 5 «recognising a session on an ordinary read», abridged` · full text: [sad.md](../sad.md)

> No `CHECK` on the ordering of the three timestamps: the 14-day and 90-day rules live on the `Session` entity in Domain, and restating them as database constraints would put one rule in two places that can disagree.
>
> — `data-model.md §Entities, Sessions constraints, verbatim` · full text: [data-model.md](../data-model.md)

> | Session expiry accuracy | session ends within 14 days plus at most 1 hour after the last activity | integration test against a controllable clock |
> | Session absolute lifetime | no session is recognised more than 90 days after it was opened, however actively it is used | integration test against a controllable clock |
>
> — `spec.md §6, NFR rows, verbatim` · full text: [spec.md](../spec.md)

> **Hard rule — domain rules live in Domain:** an invariant is enforced by the entity, not by a use case or an endpoint.
>
> — `sad.md §2, Conventions, verbatim` · full text: [sad.md](../sad.md)

> All time-dependent fixtures take their instant from the injected `IClock` port (sad §5), never from `DateTimeOffset.UtcNow`.
>
> — `data-model.md §Test fixtures, verbatim` · full text: [data-model.md](../data-model.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md)) and follow it. Do not guess.

## Data delta

No DB changes. This task declares the entity only; T4 turns it into the `Sessions` table. The entity's shape must match what T4 will persist: `Id`, `AccountId`, `CreatedAt`, `LastSeenAt`, `RevokedAt`.

— `data-model.md §Entities, table Sessions, abridged` · full text: [data-model.md](../data-model.md)

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

This task owns the verdict; T12 is what presents the sign-in form on the strength of it.

## Checklist

- [ ] `Session` entity with `Id`, `AccountId`, `CreatedAt`, `LastSeenAt`, `RevokedAt` — `src/Uniqua.Projector.Domain/Accounts/Session.cs`.
- [ ] A factory that opens a session for an account id at a given instant, allocating `Id` with `Guid.CreateVersion7()`.
- [ ] `IsExpired(DateTimeOffset now)` — true when `now - LastSeenAt >= 14 days` **or** `now - CreatedAt >= 90 days`. Takes `now` as a parameter; the entity reads no clock of its own.
- [ ] `IsRevoked` / `Revoke(DateTimeOffset at)` — revocation is a separate fact from expiry, and a revoked session is never live.
- [ ] `ShouldStampActivity(DateTimeOffset now)` — true only when `LastSeenAt` is more than one hour behind `now`, so the caller writes at most once an hour, and `StampActivity(now)`.
- [ ] Unit tests at both boundaries: 14 days minus a minute is live, 14 days exactly is not; 90 days minus a minute is live while actively used, 90 days exactly is not.

## Edge cases

| Case | Behaviour |
|---|---|
| A session is both idle for 14 days and older than 90 days | Expired — the two rules are `or`-ed, not ranked |
| A session revoked one minute ago and not expired | Not live: `IsRevoked` is checked independently of `IsExpired` |
| A session opened 89 days ago and used every day | Live — the absolute ceiling has not been reached yet |
| A session opened 90 days ago and used one second ago | Expired, because the absolute ceiling ignores activity entirely (AC-07b) |
| `now` earlier than `LastSeenAt` (clock moved backwards) | Not expired; the entity compares elapsed time and never treats a negative interval as an expiry |
| `LastSeenAt` is 59 minutes behind `now` | `ShouldStampActivity` is false — that is the 1 hour of slack spec §6 allows on expiry accuracy |

## Definition of Done

- [ ] Unit tests pass at the 14-day and 90-day boundaries, on both sides of each, driven by an injected instant rather than the system clock.
- [ ] `Session.IsExpired` is the only place in the codebase where 14 days or 90 days appears as an expiry rule.
- [ ] `src/Uniqua.Projector.Domain/` still references no other project and no framework package.
- [ ] `ShouldStampActivity` is false for anything under one hour.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
