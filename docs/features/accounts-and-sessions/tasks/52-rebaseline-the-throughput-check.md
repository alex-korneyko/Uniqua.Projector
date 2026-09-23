---
id: T52
title: "Re-baseline the sign-in throughput regression check so it measures a regression, not machine noise"
layer: "tests"
deps: []
acs: []
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/LatencyBudgetTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (second re-review) — Q-14"
status: "todo"
---

# T52 — Re-baseline the sign-in throughput regression check so it measures a regression, not machine noise

## Place in the sequence

- **Origin:** a verdict from the independent second re-review — Q-14 in [`_review/review-2026-09-23.md`](../_review/review-2026-09-23.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- none directly: a quality finding (stage 2). The Definition of Done below is the bar.

## Definition of Done

- [ ] Sign_ins_sustain_the_throughput_row_as_a_regression_check_only warms up, then takes the median of several timed batches, and its margin is set so the unchanged code passes reliably on the development machine (it measured 2.4-3.3/s against a 3.3/s floor), while a tenfold slowdown still fails; the comment says the §6 figure itself stays with the smoke run; the test passes 5 runs in a row.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
