---
id: T51
title: "Make the contention test, the test clock and the test peer addresses deterministic"
layer: "tests"
deps: ["T50"]
acs: []
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Fixtures/TestClock.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Fixtures/TestPeerAddress.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/IdentityAccountStoreTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (second re-review) — Q-13"
status: "todo"
---

# T51 — Make the contention test, the test clock and the test peer addresses deterministic

## Place in the sequence

- **Origin:** a verdict from the independent second re-review — Q-13 in [`_review/review-2026-09-23.md`](../_review/review-2026-09-23.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T50.

## Acceptance criteria

- none directly: a quality finding (stage 2). The Definition of Done below is the bar.

## Definition of Done

- [ ] The contention test forces the blind-increment fallback with a DbCommandInterceptor (or equivalent) that bumps the row before every conditional UPDATE, asserts the contended=true log line, and compares the returned count with what was written, without real-time races or busy-polls; TestClock records delays in a thread-safe collection; the parallel-burst test asserts that every non-429 response is exactly 401; TestPeerAddress hands out unique addresses from a monotonic counter (a peer is never reused within a run), each in its own IPv4 address, or its own /64 if IPv6, so T47's normalisation cannot merge two peers; the full integration suite passes 3 runs in a row.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
