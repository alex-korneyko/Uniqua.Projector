---
id: T28
title: "Raise the 48-hour cleanup alert as an error log so sad §7 monitoring can fire"
layer: "infra"
deps: []
acs: []
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/ExpiredSessionCleanupService.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/ExpiredSessionCleanupTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-26"
status: "done"
---

# T28 — Raise the 48-hour cleanup alert as an error log so sad §7 monitoring can fire

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-26 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (a shared file, see `files_hint`).

## Acceptance criteria

- none directly: a quality finding (stage 2). The Definition of Done below is the bar.

## Definition of Done

- [ ] When HasNotSucceededRecently is true after a run, the service logs an error naming the last success; a test with a failing sweep and an advanced clock observes that log entry.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
