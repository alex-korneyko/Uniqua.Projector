---
id: T59
title: "Prove the cleanup service's grace-deadline reschedule by driving ExecuteAsync itself"
layer: "tests"
deps: []
acs: []
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/ExpiredSessionCleanupTests.cs"
  - "src/Uniqua.Projector.Api/Accounts/ExpiredSessionCleanupService.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (third re-review) — T-03"
status: "done"
---

# T59 — Prove the cleanup service's grace-deadline reschedule by driving ExecuteAsync itself

## Place in the sequence

- **Origin:** a verdict from the independent third re-review — T-03 in [`_review/review-2026-09-23-2.md`](../_review/review-2026-09-23-2.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- — (no AC; a quality finding)

## Definition of Done

- [ ] A test starts ExpiredSessionCleanupService (StartAsync/StopAsync) against the fixture's TestClock with a sweep that always fails; it asserts that a delay of about StaleGracePeriod + GraceCheckMargin was requested, that a second sweep ran, and that session_cleanup_stale was logged; the test fails if the `if (LastSucceededAt is null) { DelayAsync; SweepAsync }` block in ExecuteAsync is deleted (verified by temporarily removing it during RED, then restoring); only test code, or a minimal test seam in the service, changes.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
