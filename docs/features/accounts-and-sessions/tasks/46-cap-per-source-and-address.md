---
id: T46
title: "Count failed sign-ins per source and address with a looser per-source ceiling, and skip the cap for an unresolved source"
layer: "ports"
deps: []
acs: ["AC-12", "AC-05b"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "src/Uniqua.Projector.Api/Accounts/SignInRateLimit.cs"
  - "src/Uniqua.Projector.Api/Accounts/SlidingWindowLimiter.cs"
  - "src/Uniqua.Projector.Api/DependencyInjection.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs"
  - "docs/features/accounts-and-sessions/spec.md"
  - "docs/features/accounts-and-sessions/adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (second re-review) — P-01"
status: "todo"
---

# T46 — Count failed sign-ins per source and address with a looser per-source ceiling, and skip the cap for an unresolved source

## Place in the sequence

- **Origin:** a verdict from the independent second re-review — P-01 in [`_review/review-2026-09-23.md`](../_review/review-2026-09-23.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-05b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] Failed-sign-in slots are reserved per (request source, normalised email address) — the same normalisation the account store applies — BEFORE the password is verified and released only on a successful sign-in; the per-address cap stays 20 per 15-minute sliding window, and a looser per-source ceiling across all addresses (a named constant, e.g. 100 per 15 minutes) bounds address rotation from one source; when RequestSource.Of resolves to "unknown" the cap is not applied (logged as a warning), so a missing remote address cannot make every caller share one budget; integration tests: a correct password for a different address from a source that has capped another address gets 201; the capped (source, address) pair gets 429 even with the correct password (pins the accepted residual); the per-source ceiling is refused with 429; an unknown source is never capped; spec §5 AC-12 clarifies 'never unusable to its owner' as 'never by anyone who does not share the owner's request source', and spec §6.1 and an ADR 0010 amendment record the accepted residual (a guesser sharing the owner's source and targeting the owner's address, or a source at its ceiling, can refuse the owner for up to 15 minutes).
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
