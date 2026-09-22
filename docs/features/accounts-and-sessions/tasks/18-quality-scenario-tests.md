---
id: T18
title: "Write the three quality-scenario test suites, including the anti-lockout regression"
layer: "tests"
deps: ["T9", "T12", "T13", "T14"]
blocks: []
acs: ["AC-05b", "AC-07", "AC-07b", "AC-12"]
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/SessionLifetimeTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/LatencyBudgetTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Fixtures/"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "done"
---

# T18 — Write the three quality-scenario test suites, including the anti-lockout regression

## Place in the sequence

- **Blocked by:** T9 — SignIn, T12 — session recognition, T13 — registration endpoint, T14 — sessions endpoints · **Blocks:** — · **Wave:** 6. It asserts across tasks, so it lands once the paths it measures exist.
- **Lane:** own lane — it is the only task writing under `tests/.../Quality/`.

## Why (user story)

> **As an** account
> **I want** repeated wrong-password attempts against me to become progressively futile
> **So that** a public sign-in form is not an open invitation
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

The per-task tests prove each piece behaves. This task proves the three §10 quality scenarios hold across the pieces — and installs the one regression test that catches the specific mistake sad §11 predicts someone will make.

## Inlined context

> **QG-1. Security of the single authentication boundary.** **How verify:** integration test against a controllable clock for the delay curve and the reset; a unit test over the hashing parameters for the ≥ 100 ms floor; a timing-comparison test for AC-05b; **and a regression test asserting that an account with many recent failures still accepts the correct password immediately** — that is the test which catches someone re-enabling the framework lockout ADR 0010 deliberately switched off.
>
> — `sad.md §10, QG-1, verbatim` · full text: [sad.md](../sad.md)

> **QG-2. Session continuity with a bounded lifetime.** **How verify:** integration tests against a controllable clock for the sliding window and the absolute ceiling; a post-deployment check for redeploy survival — **first verifiable at roadmap step 4**, when a real deployment exists. `plan-tests` records that third item as deferred, not as covered.
>
> — `sad.md §10, QG-2, verbatim` · full text: [sad.md](../sad.md)

> **QG-3. Unattended reachability within the latency budget.** **How verify:** server-side timing sampled in the smoke run for the three p95 figures; throughput measured on the reference machine — a 2-vCPU virtual machine on the self-hosted host — with the same smoke test in CI counting only as a regression check, since the runner is not the reference machine.
>
> — `sad.md §10, QG-3, verbatim` · full text: [sad.md](../sad.md)

> `AnAccount()` · `ALiveSession(account)` · `AnIdleSession(account)` — `LastSeenAt = now − 14 days − 1 minute`: the AC-07 boundary · `AnAgedSession(account)` — `CreatedAt = now − 90 days − 1 minute`, `LastSeenAt = now`: the AC-07b boundary, where the session is actively used and must still be refused · `ARevokedSession(account)` · `AnAccountUnderGuessing(failures)` — sets `AccessFailedCount` and `LastFailedAttemptAt` directly. **PII guard: every address is `example.test`.**
>
> — `data-model.md §Test fixtures, abridged` · full text: [data-model.md](../data-model.md)

> **Hard rule — tests:** integration through `WebApplicationFactory` against a SQL Server container, plus unit tests on Domain invariants.
>
> — `sad.md §2, Conventions, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — risk:** The framework's account lockout could be re-enabled by someone who assumes it is the safe default, silently breaking AC-12.
>
> — `sad.md §11, risk row, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([sad.md](../sad.md) · [spec.md](../spec.md) · [data-model.md](../data-model.md)) and follow it. Do not guess.

## Data delta

No schema change. This task builds the six C# fixture builders `data-model.md` §Test fixtures specifies, which set `AccessFailedCount`, `LastFailedAttemptAt`, `CreatedAt`, `LastSeenAt` and `RevokedAt` directly rather than by driving six real attempts — beside the integration tests, never as migration rows.

— `data-model.md §Test fixtures + §Seeds, abridged` · full text: [data-model.md](../data-model.md)

## API contract

No new surface. The suites drive the four existing operations — `registerAccount`, `createSession`, `deleteCurrentSession`, `getCurrentAccount` — through `WebApplicationFactory` and assert the statuses and `accounts.*` codes the contract states.

— `contracts/openapi.yaml, all operations, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-05b — error

> **Given** a visitor submitting the sign-in form with an address no account was ever registered with
> **When** they submit it with any password
> **Then** the system refuses with the same wording it uses for a wrong password and takes a comparable time to do so, so that neither the message nor the wait reveals whether the address is registered
>
> — `spec.md §5, AC-05b, verbatim` · full text: [spec.md](../spec.md)

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

### AC-12 — error

> **Given** an account against which 5 consecutive sign-in attempts have already failed
> **When** a further attempt is made with a wrong password
> **Then** the system refuses it after a delay that grows with each additional failure, while a correct password is still accepted immediately, so that guessing becomes futile without the account ever becoming unusable to its owner; the count of consecutive failures returns to zero either on a correct password or after 15 minutes in which no attempt is made at all
>
> — `spec.md §5, AC-12, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] The six fixture builders from `data-model.md` §Test fixtures, every address on `example.test` — `tests/Uniqua.Projector.Api.IntegrationTests/Fixtures/`.
- [ ] **QG-1** — the delay curve at the 6th (≥ 2 s) and 10th (≥ 30 s) failure, the 15-minute reset, the AC-05b timing comparison, and **the anti-lockout regression**: an account with many recent failures still accepts its correct password immediately.
- [ ] **QG-2** — the 14-day sliding window and the 90-day ceiling driven through `GET /api/v1/accounts/me` against the controllable clock, plus the revoked-session case; the redeploy-survival row is marked **deferred to roadmap step 4** with a skipped test naming it, not silently omitted.
- [ ] **QG-3** — server-side timing over registration, sign-in and session recognition, asserting the ≤ 800 ms / ≤ 600 ms / ≤ 30 ms p95 budgets; sign-in samples exclude attempts delayed by the guessing protection.
- [ ] The throughput row (≥ 10 sign-ins/s) is asserted only as a regression check, with a comment stating that CI is not the §6 reference machine.
- [ ] Every time-dependent test drives `IClock`; none sleeps in real time except where the delay itself is the thing under test.
- [ ] A test asserting `Lockout.AllowedForNewUsers` is false, so the configuration itself cannot drift back.

## Edge cases

| Case | Behaviour |
|---|---|
| CI is slower than the reference machine | The p95 assertions run as regression checks with generous margins; the real figures are the smoke run's on the reference machine (spec §6) |
| Docker is unavailable for the SQL Server container | Per `require_integration: auto`, the tier is reported NON-red rather than passing silently |
| The AC-05b timing comparison is flaky under load | Compare distributions with a margin rather than single samples; a flaky oracle is worse than a coarse one |
| A delay test would sleep 30 real seconds | Drive the delay through the injected clock; only assert the real wait once, at the smallest floor |
| The redeploy-survival check cannot run | An explicitly skipped test naming roadmap step 4 — deferred, never quietly dropped (spec §5, sad §10) |
| Someone re-enables the framework lockout | The QG-1 regression test fails on the correct-password-after-many-failures case. That is the whole point of it |

## Definition of Done

- [ ] All three suites pass through `WebApplicationFactory` against the SQL Server container.
- [ ] The anti-lockout regression test exists, and fails if `Lockout.AllowedForNewUsers` is set true (verified by flipping it once, locally).
- [ ] The delay-curve, reset, 14-day and 90-day tests all drive `IClock` rather than the system clock.
- [ ] The AC-05b timing comparison asserts comparable elapsed time between an unknown address and a wrong password.
- [ ] The redeploy-survival row appears as an explicitly skipped test citing roadmap step 4, so the gap is visible in the test output.
- [ ] Every fixture address is on `example.test`.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
