---
id: T49
title: "Raise the stale-cleanup alert after a short grace period when no sweep has ever succeeded"
layer: "infra"
deps: []
acs: []
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/ExpiredSessionCleanupService.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/ExpiredSessionCleanupTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (second re-review) — Q-09"
status: "done"
---

# T49 — Raise the stale-cleanup alert after a short grace period when no sweep has ever succeeded

## Place in the sequence

- **Origin:** a verdict from the independent second re-review — Q-09 in [`_review/review-2026-09-23.md`](../_review/review-2026-09-23.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- none directly: a quality finding (stage 2). The Definition of Done below is the bar.

## Definition of Done

- [ ] Before the first successful sweep, staleness is measured against a short named grace period after process start (e.g. one hour) instead of the full 48-hour HealthyInterval, so a sweep that always fails on an instance restarted more often than every 48 hours still raises session_cleanup_stale; after a success the 48-hour rule is unchanged; tests: a first failure at t=0 raises no alert, a failure after the grace period with no success ever raises it, and a failure 47 hours after a success raises none.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
