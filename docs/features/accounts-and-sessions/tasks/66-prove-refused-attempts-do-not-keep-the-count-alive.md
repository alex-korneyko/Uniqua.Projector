---
id: T66
title: "Prove AC-12's reset clause: a run of 429s does not keep the failure count alive"
layer: "tests"
deps: ["T62"]
acs: ["AC-12"]
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (fourth re-review) — U-06"
status: "todo"
---

# T66 — Prove AC-12's reset clause: a run of 429s does not keep the failure count alive

## Place in the sequence

- **Origin:** a verdict from the independent fourth re-review — U-06 in [`_review/review-2026-09-23-3.md`](../_review/review-2026-09-23-3.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T62.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] An integration test on TestClock: 20 wrong passwords cap the (source, address) pair; further attempts at it keep answering 429 until 15 minutes after the last failure that reached verification; once the per-pair window has also passed, the next wrong password is verified and counted as failure #1 (no delay applied, AccessFailedCount = 1), showing that the 429s alone did not keep the count alive, exactly as spec.md §5 AC-12 now reads; the test fails if a 429 were made to touch LastFailedAttemptAt or AccessFailedCount (verified during RED by a temporary mutation, then restored); only test code changes.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
