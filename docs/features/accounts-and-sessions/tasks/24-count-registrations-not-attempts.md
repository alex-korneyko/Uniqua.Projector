---
id: T24
title: "Count only accepted registrations against the per-source limit and prune stale sources"
layer: "ports"
deps: []
acs: ["AC-01b"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/RegistrationRateLimit.cs"
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RegisterEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-11, R-25"
status: "todo"
---

# T24 — Count only accepted registrations against the per-source limit and prune stale sources

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-11, R-25 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (a shared file, see `files_hint`).

## Acceptance criteria

- AC-01b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] Five refused submissions followed by a valid one from the same source returns 201; the sixth accepted registration within a minute is still 429 with Retry-After; entries older than the window are pruned so the dictionary does not grow without bound (tested).
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
