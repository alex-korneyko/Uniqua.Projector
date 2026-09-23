---
id: T48
title: "Give a malformed body its declared problem in every environment, and declare coded 500 and 415 problems"
layer: "ports"
deps: []
acs: ["AC-10"]
files_hint:
  - "src/Uniqua.Projector.Api/ProblemDetailsSetup.cs"
  - "src/Uniqua.Projector.Api/AccountProblems.cs"
  - "docs/features/accounts-and-sessions/contracts/openapi.yaml"
  - "tests/Uniqua.Projector.Api.IntegrationTests/AccountProblemsTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/RequestShapeTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (second re-review) — P-05"
status: "done"
---

# T48 — Give a malformed body its declared problem in every environment, and declare coded 500 and 415 problems

## Place in the sequence

- **Origin:** a verdict from the independent second re-review — P-05 in [`_review/review-2026-09-23.md`](../_review/review-2026-09-23.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- AC-10 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] A malformed or missing JSON body on POST /api/v1/accounts and POST /api/v1/sessions answers 400 accounts.request_malformed in Development too (where minimal APIs set ThrowOnBadRequest=true) — handled in the one ProblemDetails handler, not per endpoint; the unhandled-exception 500 carries a stable code (e.g. accounts.unexpected, via the AccountProblems wording table) and is declared as a shared response on every accounts operation in openapi.yaml; a wrong Content-Type on those two operations answers the declared, coded 400 accounts.request_malformed (or a declared coded 415); AccountProblemsTests covers every new row; a new test boots the host with UseEnvironment("Development") and asserts the malformed-body 400 and its code.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
