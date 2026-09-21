---
id: T20
title: "Add the expired-session cleanup hosted service with its startup and daily runs"
layer: "infra"
deps: ["T4", "T6"]
blocks: []
acs: []
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/ExpiredSessionCleanupService.cs"
  - "src/Uniqua.Projector.Api/Program.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/ExpiredSessionCleanupTests.cs"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "todo"
---

# T20 — Add the expired-session cleanup hosted service with its startup and daily runs

## Place in the sequence

- **Blocked by:** T4 — the sessions table and its two sweep indexes, T6 — `ISessionStore.DeleteExpired` · **Blocks:** — · **Wave:** 4, in parallel with the use cases.
- **Lane:** shares `src/Uniqua.Projector.Api/Program.cs` with T11, T12 and T13 — serialized in that lane.

## Why (user story)

> **As an** account
> **I want** my session to survive closing the browser
> **So that** returning to the link days later does not cost me a sign-in
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

A session row records when a named person signed in. This task bounds how long that record lives, in a product that has no deletion path at all.

**No §5 acceptance criterion covers this task** — `acs` is deliberately empty, and sad §6 flow 7 labels it "no acceptance criterion - hygiene". It exists because of spec §3's absence of a deletion route, quoted below.

## Inlined context

> **Expired-session cleanup.** A hosted background service inside the API process removes rows that can no longer be live — older than 90 days, or revoked more than 14 days ago — once a day and once at startup. The startup run matters because the daily timer does not survive a restart: a redeploy, a reboot of the host or a crash all reset it, and on a manually-operated instance those are the common case rather than the exception. Cleanup is **hygiene, not enforcement**: an expired row is refused at recognition time regardless, because the `Session` entity itself decides it is dead. It exists because a session row records when a named person signed in, and spec §3 rules out any data-deletion path — without cleanup the table is a permanent visit log.
>
> — `sad.md §7, Deployment view, verbatim` · full text: [sad.md](../sad.md)

> `Cleanup->>Cleanup: Skip this run if the previous one is still in progress - the idempotency guard, since the work has no key of its own` → `Infra->>Db: Delete sessions opened more than 90 days ago, or revoked more than 14 days ago` → `Cleanup->>Cleanup: Record the count and the time this run succeeded, for the section 7 monitoring` → *note:* A failed run is not retried immediately - it is simply attempted again at the next start or the next day, because the sweep is idempotent and a missed run costs nothing but table size → `alt No run has succeeded for more than 48 hours` → `Cleanup->>Ops: Raise the section 7 alert`
>
> — `sad.md §6, flow 7 «expired-session cleanup», abridged` · full text: [sad.md](../sad.md)

> *Postcondition:* cleanup is hygiene, never enforcement - an expired session is refused at recognition time regardless, because the Session entity itself decides it is dead (flow 5)
>
> — `sad.md §6, flow 7 postcondition, verbatim` · full text: [sad.md](../sad.md)

> **Delete strategy:** hard delete, by the cleanup sweep only (sad §7). Expiry itself is *not* a delete — an expired row is refused at recognition time and removed later.
>
> — `data-model.md §Entities, Sessions, verbatim` · full text: [data-model.md](../data-model.md)

> **Hard rule — monitoring and alerts:** Count of live session records; count of rows removed by the last cleanup run, and when it last succeeded. Alert: cleanup has not succeeded for more than 48 hours.
>
> — `sad.md §7, Monitoring + Alerts, abridged` · full text: [sad.md](../sad.md)

> **A session row records when a named person signed in.** The §7 cleanup bounds how long that history lives, but there is no per-person erasure.
>
> — `sad.md §11, accepted debt, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([sad.md](../sad.md) · [data-model.md](../data-model.md)) and follow it. Do not guess.

## Data delta

No schema change. This is the only code in the product that deletes a row:

| Column | Type | Constraints | Change |
|---|---|---|---|
| `Sessions.CreatedAt` | `datetimeoffset` | NOT NULL, `IX_Sessions_CreatedAt` | filtered on — rows opened more than 90 days ago |
| `Sessions.RevokedAt` | `datetimeoffset` | NULL, `IX_Sessions_RevokedAt` (filtered) | filtered on — rows revoked more than 14 days ago |

Both indexes exist for exactly this sweep (T4).

— `data-model.md §Entities + §Indexes, Sessions, abridged` · full text: [data-model.md](../data-model.md)

## API contract

Internal — no API surface. The sweep is a background service; nothing triggers it over HTTP, and no endpoint reports it.

## Acceptance criteria

None. sad §6 flow 7 states it outright — "no acceptance criterion - hygiene, from section 7". The obligations this task must meet are the §7 sentence quoted above (startup run plus daily run, idempotent, hygiene not enforcement) and the §7 monitoring row; both are asserted in the Definition of Done.

## Checklist

- [ ] `ExpiredSessionCleanupService : BackgroundService` — one run at startup, then once a day — `src/Uniqua.Projector.Api/Accounts/ExpiredSessionCleanupService.cs`.
- [ ] Each run calls `ISessionStore.DeleteExpired()` (T6); no query is written here, because only Infrastructure may issue one.
- [ ] An in-process guard that skips a run while the previous one is still going (flow 7's idempotency guard — the work has no key of its own).
- [ ] Record, per run: the row count removed and the time the run succeeded — the two §7 monitoring figures.
- [ ] No immediate retry on failure: log it and let the next start or the next day handle it.
- [ ] Emit the "no successful run for more than 48 hours" condition as a metric an alert can watch; do not build an alerting mechanism here.
- [ ] Register in `Program.cs`, and make sure a failing sweep can never take the application down with it.
- [ ] Integration tests: an aged row and a long-revoked row are removed; a live row and a recently-revoked row are not; a no-op run succeeds.

## Edge cases

| Case | Behaviour |
|---|---|
| A run is still going when the daily timer fires | Skipped — the guard holds; a missed run costs nothing but table size |
| The sweep throws (database unavailable) | Logged, not retried immediately; the application keeps serving, since cleanup is hygiene and never enforcement |
| The instance restarts every hour | The startup run fires each time; the sweep is idempotent, so repeated runs are harmless |
| The instance runs for weeks without restarting | The daily timer carries it; the startup run is the fallback for the common restart case, not the mechanism |
| A row is expired but not yet swept | Still refused at recognition time (T12) — the sweep is not what makes a session dead |
| A revoked row 13 days old | Kept — AC-10's refusal is answered by that record until 14 days have passed |
| The sweep deletes nothing on every run | A success, and normal at invited-reviewer scale |
| Cleanup has not succeeded for 49 hours | The metric shows it, and the §7 alert is what escalates — there is no dead-letter queue and nothing to replay |

## Definition of Done

- [ ] An integration test proves rows opened more than 90 days ago and rows revoked more than 14 days ago are removed, and that live and recently-revoked rows survive.
- [ ] An integration test proves the guard prevents a second concurrent run.
- [ ] An integration test proves a failing sweep neither stops the host nor blocks a subsequent run.
- [ ] The run's removed-row count and last-success time are observable as metrics, and a "no success in 48 hours" condition is derivable from them.
- [ ] The service issues no query of its own — every deletion goes through `ISessionStore.DeleteExpired`.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
