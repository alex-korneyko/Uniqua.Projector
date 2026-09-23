---
id: T56
title: "Prove the tracked-key ceiling's fail-open contract for both limiters, including the sign-in per-address limiter"
layer: "tests"
deps: ["T55"]
acs: ["AC-12", "AC-01b"]
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RequestSourceTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (third re-review) — T-04, S-02"
status: "todo"
---

# T56 — Prove the tracked-key ceiling's fail-open contract for both limiters, including the sign-in per-address limiter

## Place in the sequence

- **Origin:** a verdict from the independent third re-review — T-04, S-02 in [`_review/review-2026-09-23-2.md`](../_review/review-2026-09-23-2.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T55.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-01b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] RequestSourceTests (or a new ceiling test class) asserts, for RegistrationRateLimit and for both SignInRateLimit limiters, with the limiter filled to Capacity: a new key is permitted and not counted; the ceiling is logged at error level; a key already tracked is still refused past its own limit; after TestClock passes Window a new key is tracked again; for the sign-in per-address limiter specifically, a new pair past the ceiling is still bounded by the per-source ceiling of 100 (the fallback spec §6.1 will name) — every assertion fails if the limiter refused new keys at the ceiling, if the forced reclaim were deleted, or if the ceiling log were removed.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
