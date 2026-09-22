---
id: T37
title: "Seed the failure compare-and-set with the count already read, and return the count actually written under contention"
layer: "infra"
deps: []
acs: ["AC-12", "AC-05b"]
files_hint:
  - "src/Uniqua.Projector.Application/Accounts/Ports/IAccountStore.cs"
  - "src/Uniqua.Projector.Application/Accounts/SignIn.cs"
  - "src/Uniqua.Projector.Infrastructure/Accounts/IdentityAccountStore.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/IdentityAccountStoreTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SignInTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 (re-review) — N-09, N-10"
status: "done"
---

# T37 — Seed the failure compare-and-set with the count already read, and return the count actually written under contention

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent re-review — N-09, N-10 in [`_review/review-2026-09-22-2.md`](../_review/review-2026-09-22-2.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (shared files, see `files_hint`).

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-05b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] RecordFailureAsync takes the ConsecutiveFailures / LastFailedAttemptAt already read by FindByEmailAsync as its first compare-and-set expectation, so an uncontended wrong password costs one UPDATE and no extra SELECT; only a missed write re-reads; after the retry budget the blind increment returns the value actually stored (read back, or OUTPUT inserted), never the stale `next`; the reset rule stays in GuessingDelay (Domain); tests: the uncontended path issues no re-read (observable through a counting interceptor or equivalent), the contended fallback returns the stored count, 6 parallel failures still get 1..6.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
