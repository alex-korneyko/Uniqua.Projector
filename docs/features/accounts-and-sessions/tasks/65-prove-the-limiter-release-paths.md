---
id: T65
title: "Prove that a per-address refusal gives back its per-source slot, and that Release racing Reserve loses no reservation"
layer: "tests"
deps: ["T64"]
acs: ["AC-12"]
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SignInRateLimitTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RequestSourceTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (fourth re-review) — V-05, V-06"
status: "todo"
---

# T65 — Prove that a per-address refusal gives back its per-source slot, and that Release racing Reserve loses no reservation

## Place in the sequence

- **Origin:** a verdict from the independent fourth re-review — V-05, V-06 in [`_review/review-2026-09-23-3.md`](../_review/review-2026-09-23-3.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T64.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] SignInRateLimitTests caps one (source, address) pair at 20, sends about 100 more attempts at it that the per-address cap refuses, then asserts that a Reserve for another address from the same source is permitted and that the source's per-source count still reflects only the permitted attempts — the test fails if the _perSource.Release call on a per-address refusal in SignInRateLimit.Reserve is deleted (verified during RED by removing it, then restoring); a multi-threaded test runs Reserve and Release concurrently on one key of a SlidingWindowLimiter and asserts that, once the threads finish, the reservations never released still bring the key to a refusal at PermittedPerWindow — the test fails if the ReferenceEquals re-check under the list lock is removed (verified the same way, or explained if the race cannot be forced deterministically); only test code changes.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
