---
id: T55
title: "Throttle the tracked-key ceiling path: rate-limit the forced prune, log the ceiling once per window, and count keys without locking every bucket"
layer: "ports"
deps: ["T54"]
acs: ["AC-12", "AC-01b"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/SlidingWindowLimiter.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RequestSourceTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (third re-review) — T-01"
status: "done"
---

# T55 — Throttle the tracked-key ceiling path: rate-limit the forced prune, log the ceiling once per window, and count keys without locking every bucket

## Place in the sequence

- **Origin:** a verdict from the independent third re-review — T-01 in [`_review/review-2026-09-23-2.md`](../_review/review-2026-09-23-2.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T54.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-01b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] In SlidingWindowLimiter, the forced PruneExpired a new key triggers at Capacity runs at most once per a named short interval (e.g. one second), claimed through a compare-and-set like PruneIfDue's, so a limiter kept full costs a full scan at most once per interval rather than once per request; the tracked_source_ceiling_reached error is logged at most once per window, carrying how many keys went untracked since the last line; the capacity check uses an approximate count kept with Interlocked on add and remove instead of ConcurrentDictionary.Count, and TrackedSourceCount keeps its meaning for tests; fail-open at the ceiling (permitted, not counted) is unchanged; tests: with the limiter at Capacity, 1,000 new keys within one interval cause at most one forced prune and at most one ceiling log line, and a key is tracked again once the clock passes the window.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
