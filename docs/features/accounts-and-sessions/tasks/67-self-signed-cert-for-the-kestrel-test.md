---
id: T67
title: "Give the real-Kestrel request-shape test its own throwaway certificate, and correct its comment"
layer: "tests"
deps: []
acs: []
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/RequestShapeTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (fourth re-review) — V-04"
status: "done"
---

# T67 — Give the real-Kestrel request-shape test its own throwaway certificate, and correct its comment

## Place in the sequence

- **Origin:** a verdict from the independent fourth re-review — V-04 in [`_review/review-2026-09-23-3.md`](../_review/review-2026-09-23-3.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- None — this task proves or tidies existing behaviour; see the finding in the review record.

## Definition of Done

- [ ] The real-socket Kestrel fixture in RequestShapeTests builds a self-signed certificate in the test with System.Security.Cryptography.X509Certificates.CertificateRequest (no new package) and passes it to UseHttps, so the 413 test no longer depends on an ASP.NET Core dev certificate being installed on the machine or the CI runner; the client trusts only that certificate (or the test's handler accepts that exact certificate); the class and helper comments describe what the code actually does; the 413 test and the 408/411/431 tests stay green.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
