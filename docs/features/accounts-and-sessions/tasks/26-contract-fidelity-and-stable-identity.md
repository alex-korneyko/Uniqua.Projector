---
id: T26
title: "Return the stored address from register and sign-in, declare display_name_invalid, and prove the id is stable across the account's life"
layer: "ports"
deps: ["T22", "T24"]
acs: ["AC-01", "AC-04", "AC-13"]
files_hint:
  - "src/Uniqua.Projector.Application/Accounts/RegisterAccount.cs"
  - "src/Uniqua.Projector.Application/Accounts/SignIn.cs"
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "docs/features/accounts-and-sessions/contracts/openapi.yaml"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-15, R-16"
status: "done"
---

# T26 — Return the stored address from register and sign-in, declare display_name_invalid, and prove the id is stable across the account's life

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-15, R-16 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** T22, T24 (a shared file, see `files_hint`).

## Acceptance criteria

- AC-01 — verbatim in [spec.md §5](../spec.md)
- AC-04 — verbatim in [spec.md §5](../spec.md)
- AC-13 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] Signing in with a differently-cased, padded address returns the email exactly as /me returns it; openapi.yaml declares accounts.display_name_invalid; one test registers, signs out, fails 3 times and signs in again, asserting the same id at every step.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
