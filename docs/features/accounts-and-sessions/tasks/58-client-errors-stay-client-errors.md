---
id: T58
title: "Answer a non-400 bad request with its own 4xx in every environment, and rename the shared 500 to a feature-neutral api.unexpected"
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
origin: "review 2026-09-23 (third re-review) — T-02, T-06"
status: "done"
---

# T58 — Answer a non-400 bad request with its own 4xx in every environment, and rename the shared 500 to a feature-neutral api.unexpected

## Place in the sequence

- **Origin:** a verdict from the independent third re-review — T-02, T-06 in [`_review/review-2026-09-23-2.md`](../_review/review-2026-09-23-2.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- AC-10 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] In the one handler (ProblemDetailsSetup), a BadHttpRequestException with any 4xx status other than 400 — 413 for a body over Kestrel's limit, among others — answers that same status as a coded problem (a declared code for 413, e.g. api.request_too_large, or the existing request_malformed family if openapi is extended to say so), logged at Information or Warning, never as unhandled_exception, in Development as well as elsewhere; the code the one handler gives every unhandled exception is renamed from accounts.unexpected to api.unexpected, with type URI .../problems/api/unexpected, in AccountProblems (or a neutral problems table beside it), openapi.yaml components/responses/Unexpected and every test that names it; AccountProblemsTests covers the new and renamed rows; tests: a Development-boot request with a body over the configured limit (set a small MaxRequestBodySize in the test host) answers 413 with its code, and /boom answers 500 api.unexpected.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
