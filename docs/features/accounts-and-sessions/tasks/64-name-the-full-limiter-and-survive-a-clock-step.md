---
id: T64
title: "Name which limiter hit its ceiling, give each its true consequence, and keep the throttles working after the clock steps back"
layer: "ports"
deps: []
acs: ["AC-12", "AC-01b"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/SlidingWindowLimiter.cs"
  - "src/Uniqua.Projector.Api/Accounts/SignInRateLimit.cs"
  - "src/Uniqua.Projector.Api/Accounts/RegistrationRateLimit.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SignInRateLimitCeilingTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RequestSourceTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (fourth re-review) — V-02, V-03"
status: "done"
---

# T64 — Name which limiter hit its ceiling, give each its true consequence, and keep the throttles working after the clock steps back

## Place in the sequence

- **Origin:** a verdict from the independent fourth re-review — V-02, V-03 in [`_review/review-2026-09-23-3.md`](../_review/review-2026-09-23-3.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-01b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] SlidingWindowLimiter takes a limiter name and a consequence from its owner, and the tracked_source_ceiling_reached line carries limiter={Limiter} (registration, sign_in_per_source, sign_in_per_address) and that limiter's own consequence — the per-address limiter says the pair goes uncapped while the per-source cap of 100 still applies, never source_not_rate_limited; the forced-prune throttle, the ceiling-log throttle and PruneIfDue treat a negative elapsed time (the clock stepped backwards) as due rather than not due, or are stamped from a monotonic source, so a backwards clock step cannot switch off the forced reclaim or the ceiling alarm; tests: SignInRateLimitCeilingTests asserts each sign-in limiter's ceiling line names its limiter and its consequence, and RegistrationRateLimit's names registration; with the limiter at Capacity, stepping TestClock back by an hour still lets the next new key trigger the forced reclaim and the ceiling line.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
