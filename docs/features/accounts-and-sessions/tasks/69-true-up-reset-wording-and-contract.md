---
id: T69
title: "Bring sad, ADR 0010, data-model, README, test-plan and openapi in line with the reset rule, the length guard, antiforgery and the rejection statuses"
layer: "docs"
deps: ["T62", "T63", "T64"]
acs: ["AC-12", "AC-05b"]
files_hint:
  - "docs/features/accounts-and-sessions/sad.md"
  - "docs/features/accounts-and-sessions/adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md"
  - "docs/features/accounts-and-sessions/data-model.md"
  - "README.md"
  - "src/Uniqua.Projector.Domain/Accounts/GuessingDelay.cs"
  - "docs/features/accounts-and-sessions/test-plan.md"
  - "docs/features/accounts-and-sessions/contracts/openapi.yaml"
  - "docs/features/accounts-and-sessions/contracts/api-sync-report.md"
  - "tests/Uniqua.Projector.Api.IntegrationTests/SessionRulesDocumentTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (fourth re-review) — U-01, U-03, U-04, U-05"
status: "done"
---

# T69 — Bring sad, ADR 0010, data-model, README, test-plan and openapi in line with the reset rule, the length guard, antiforgery and the rejection statuses

## Place in the sequence

- **Origin:** a verdict from the independent fourth re-review — U-01, U-03, U-04, U-05 in [`_review/review-2026-09-23-3.md`](../_review/review-2026-09-23-3.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T62, T63, T64.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-05b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] U-01: sad.md flow 6 and §10 QG-1, ADR 0010's decision-driver and consequence bullets, data-model.md's LastFailedAttemptAt rationale, README.md and the GuessingDelay.cs doc comments all say the count resets after 15 minutes in which no failed attempt reached verification (no 'no attempt at all' or 'idle' left), and SessionRulesDocumentTests also checks README.md for that wording; U-04: sad flow 4 has an alt branch before Reserve for an email over 256 or a password over 128 characters (same 401 credentials_invalid, nothing reserved, measured on the trimmed address per T62), and test-plan.md has an edge case for 257/129 characters -> 401, identical body, no slot taken; U-03: openapi declares the readable XSRF-TOKEN Set-Cookie on GET /api/v1/accounts/me, describes the cookie-and-header pair in the AntiforgeryToken parameter, drops the '# unresolved — OQ-API-1' marker, and contracts/api-sync-report.md records OQ-API-1 as closed; U-05: openapi's RequestRejected keeps only examples that reach the application (408, 405), says that 411, 414 and 431 are answered by Kestrel with no problem body, and GET /api/v1/accounts/me and DELETE /api/v1/sessions/current declare the 4XX RequestRejected response; openapi also records that unknown request fields are refused (T63) and the ceiling log names its limiter (T64) wherever it already describes those behaviours.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
