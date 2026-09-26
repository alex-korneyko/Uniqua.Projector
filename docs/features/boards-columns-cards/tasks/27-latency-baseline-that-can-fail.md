---
id: T27
title: "Record the latency baseline only on a passing run and compare against the last recorded run"
layer: "tests"
deps: []
acs: []
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardLatencyBudgetTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/.baselines/board-latency-budget.json"
owner: "Alex Korneiko"
estimate: "S"
status: "todo"
origin: "review 2026-09-26 — findings Q3, Q6 (timing note)"
---

# T27 — Record the latency baseline only on a passing run and compare against the last recorded run

## Place in the sequence

- **Origin:** follow-up from the independent review, findings Q3, Q6 (timing note) — [review-2026-09-26.md](../_review/review-2026-09-26.md). The finding text there is the source; this brief restates it.
- **Blocked by:** —

## What is wrong and what to do

- **Q3** — the baseline is written unconditionally, before the assertions, as `Max(previous, current)` for p95s and `Min` for throughput (`BoardLatencyBudgetTests.cs:407-414,447-448`), so it only loosens and a failing run lowers its own bar. spec §8 (fourth question) says CI fails on a p95 more than 25% slower than **the last recorded run**. Compare against the last recorded run; write the new figures only after every assertion passed; keep the file as a deliberately committed artefact (document how to refresh it, e.g. an env var gate such as `BOARD_LATENCY_RECORD=1`, so an ordinary local run does not dirty the tree).
- **Pacing** — the comment that pacing keeps each account "under 120/minute" is wrong under the frozen `TestClock`: slots never expire during the run. Advance the test clock in step with the workload (or document the total-count bound and assert it).
- **Q6** — timing is client-side (`:84-90`) while spec §6 says server-side. Keep it as a regression check but say so in the test's doc comment.

## Acceptance criteria

No spec §5 AC; the contract/spec §8 wording named above is the source.

**Fallback:** if this brief is insufficient or contradicted by the code, read the cited file:line, [spec.md](../spec.md), [sad.md](../sad.md), [contracts/openapi.yaml](../contracts/openapi.yaml), [screens.md](../screens.md) and [test-plan.md](../test-plan.md). Do not guess.

## Definition of Done

- [ ] A test (RED first) proves a regressed run fails and leaves the recorded baseline unchanged, and an ordinary run does not rewrite the committed file. The smoke run still passes on the reference machine.
- [ ] Test written first and seen failing for the right reason (quote the failing line).
- [ ] Every CLAUDE.md rule still holds (layer direction, domain rules in Domain, ProblemDetails from the one handler, Tailwind only, permissive licences).
