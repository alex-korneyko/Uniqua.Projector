---
id: T41
title: "Measure cleanup staleness from the last success or the process start, so one failed startup sweep raises no alert"
layer: "infra"
deps: []
acs: []
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/ExpiredSessionCleanupService.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/ExpiredSessionCleanupTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 (re-review) — N-08"
status: "done"
---

# T41 — Measure cleanup staleness from the last success or the process start, so one failed startup sweep raises no alert

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent re-review — N-08 in [`_review/review-2026-09-22-2.md`](../_review/review-2026-09-22-2.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (shared files, see `files_hint`).

## Acceptance criteria

- none directly: a quality finding (stage 2). The Definition of Done below is the bar.

## Definition of Done

- [ ] HasNotSucceededRecently compares clock.UtcNow against LastSucceededAt ?? the instant the service was constructed; a first sweep that fails at t=0 raises no session_cleanup_stale error; a process that has never succeeded for more than 48 hours does; the existing success-then-49-hours test still holds.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
