---
id: T38
title: "Name both password bounds, lock every declared problem in the contract test, and give a malformed body a declared problem"
layer: "ports"
deps: ["T35"]
acs: ["AC-01", "AC-02"]
files_hint:
  - "src/Uniqua.Projector.Domain/Accounts/AccountErrors.cs"
  - "src/Uniqua.Projector.Api/AccountProblems.cs"
  - "src/Uniqua.Projector.Api/ProblemDetailsSetup.cs"
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "docs/features/accounts-and-sessions/contracts/openapi.yaml"
  - "tests/Uniqua.Projector.Api.IntegrationTests/AccountProblemsTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RegisterEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 (re-review) — N-02, N-06"
status: "done"
---

# T38 — Name both password bounds, lock every declared problem in the contract test, and give a malformed body a declared problem

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent re-review — N-02, N-06 in [`_review/review-2026-09-22-2.md`](../_review/review-2026-09-22-2.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** T35 (shared files, see `files_hint`).

## Acceptance criteria

- AC-01 — verbatim in [spec.md §5](../spec.md)
- AC-02 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] The password_invalid detail names both bounds ("between 8 and 128 characters"), in AccountErrors, openapi.yaml and the AccountProblemsTests row, and the stale comment pointing at AccountProblems is removed; a 129-character password gets that refusal (RegisterEndpointTests); AccountProblemsTests has a display_name_invalid row, its stale "not in contracts" comment is gone, and it compares the full detail rather than StartsWith; the openapi 429 example matches what AccountProblems emits; a missing or malformed JSON body on POST /api/v1/accounts and POST /api/v1/sessions is answered with a problem carrying a declared `code` and status through the one ProblemDetails handler (never a 500, never a problem without a code), declared in openapi.yaml; RegisterEndpointTests:161 asserts the exact status and code.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
