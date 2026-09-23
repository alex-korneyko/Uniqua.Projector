---
id: T47
title: "Normalise request sources (IPv4-mapped to IPv4, IPv6 to its /64) and bound how many sources a limiter tracks"
layer: "ports"
deps: ["T46"]
acs: ["AC-12", "AC-01b"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "src/Uniqua.Projector.Api/Accounts/SlidingWindowLimiter.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RequestSourceTests.cs"
  - "docs/features/accounts-and-sessions/spec.md"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (second re-review) — P-02"
status: "done"
---

# T47 — Normalise request sources (IPv4-mapped to IPv4, IPv6 to its /64) and bound how many sources a limiter tracks

## Place in the sequence

- **Origin:** a verdict from the independent second re-review — P-02 in [`_review/review-2026-09-23.md`](../_review/review-2026-09-23.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T46.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-01b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] RequestSource.Of maps an IPv4-mapped IPv6 address to its IPv4 form and keys any other IPv6 address by its /64 prefix, for both the registration limit and the sign-in cap; SlidingWindowLimiter holds at most a named maximum of tracked keys (pruning expired entries first); past it, a new key goes uncounted and an Error is logged, matching the InMemoryUnknownAddressAttempts precedent, and that residual is recorded in spec §6.1; tests: two addresses in one /64 share a budget, two different /64s do not, ::ffff:a.b.c.d and a.b.c.d share a budget, and the tracked-key ceiling holds under a flood of distinct keys.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
