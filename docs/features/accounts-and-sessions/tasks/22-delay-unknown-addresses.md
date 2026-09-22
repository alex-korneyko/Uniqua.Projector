---
id: T22
title: "Hold refusals for unregistered addresses on the same curve, so the wait does not reveal registration"
layer: "app"
deps: ["T21"]
acs: ["AC-05b", "AC-12"]
files_hint:
  - "src/Uniqua.Projector.Application/Accounts/SignIn.cs"
  - "src/Uniqua.Projector.Application/Accounts/Ports/IUnknownAddressAttempts.cs"
  - "src/Uniqua.Projector.Infrastructure/Accounts/InMemoryUnknownAddressAttempts.cs"
  - "src/Uniqua.Projector.Infrastructure/DependencyInjection.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-04"
status: "done"
---

# T22 — Hold refusals for unregistered addresses on the same curve, so the wait does not reveal registration

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-04 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** T21 (a shared file, see `files_hint`).

## Acceptance criteria

- AC-05b — verbatim in [spec.md §5](../spec.md)
- AC-12 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] After N wrong attempts against an unregistered address, the (N+1)th is held by the same GuessingDelay curve a registered address gets; the counter is in memory, keyed by a hash of the normalised address, never persisted; an integration test compares both paths after 5 failures.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
