---
id: T21
title: "Serve the AC-12 delay from the attempt being made, reset the stored count after the quiet period, and record a failure before waiting"
layer: "app"
deps: []
acs: ["AC-12"]
files_hint:
  - "src/Uniqua.Projector.Domain/Accounts/GuessingDelay.cs"
  - "src/Uniqua.Projector.Application/Accounts/SignIn.cs"
  - "src/Uniqua.Projector.Application/Accounts/Ports/IAccountStore.cs"
  - "src/Uniqua.Projector.Infrastructure/Accounts/IdentityAccountStore.cs"
  - "tests/Uniqua.Projector.Domain.Tests/Accounts/GuessingDelayTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SignInTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs"
  - "docs/features/accounts-and-sessions/spec.md"
  - "docs/features/accounts-and-sessions/adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md"
  - "docs/session-rules.md"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-01, R-02, R-03, R-05"
status: "done"
---

# T21 — Serve the AC-12 delay from the attempt being made, reset the stored count after the quiet period, and record a failure before waiting

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-01, R-02, R-03, R-05 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (a shared file, see `files_hint`).

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] With 5 prior failures the next wrong attempt is held >= 2 s and with 9 prior failures >= 30 s; after 15 quiet minutes a failure stores count 1, so the next typo is free; a failure is persisted even when the client abandons the delayed request; the 5-minute ceiling is recorded in spec §6 and ADR 0010; tests assert real floors (no `>= 0`).
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
