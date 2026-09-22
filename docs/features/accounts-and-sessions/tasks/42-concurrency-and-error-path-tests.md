---
id: T42
title: "Prove the registration limit holds under parallel submissions, and that an unexpected Identity error is not reported as a uniqueness refusal"
layer: "tests"
deps: ["T35", "T37", "T38"]
acs: ["AC-01b"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/RegistrationRateLimit.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RegisterEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/IdentityAccountStoreTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 (re-review) — N-11"
status: "todo"
---

# T42 — Prove the registration limit holds under parallel submissions, and that an unexpected Identity error is not reported as a uniqueness refusal

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent re-review — N-11 in [`_review/review-2026-09-22-2.md`](../_review/review-2026-09-22-2.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** T35, T37, T38 (shared files, see `files_hint`).

## Acceptance criteria

- AC-01b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] A test runs more than 5 parallel Reserve calls for one source on a test clock and asserts exactly PermittedPerWindow are permitted; Release of a refused decision is a no-op; an unexpected Identity error from CreateAsync surfaces as a 500 problem (not email_taken or display_name_taken) through a test double; if T35 extracted a shared limiter, the same concurrency test covers it.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
