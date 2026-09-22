---
id: T23
title: "Make the session cookie persistent and add the activity-stamp slack after the 14 days, not before"
layer: "domain"
deps: []
acs: ["AC-06", "AC-07"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/SessionCookie.cs"
  - "src/Uniqua.Projector.Domain/Accounts/Session.cs"
  - "tests/Uniqua.Projector.Domain.Tests/Accounts/SessionTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RegisterEndpointTests.cs"
  - "docs/features/accounts-and-sessions/contracts/openapi.yaml"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-06, R-07"
status: "done"
---

# T23 — Make the session cookie persistent and add the activity-stamp slack after the 14 days, not before

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-06, R-07 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (a shared file, see `files_hint`).

## Acceptance criteria

- AC-06 — verbatim in [spec.md §5](../spec.md)
- AC-07 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] Set-Cookie from register and sign-in carries a Max-Age equal to Session.AbsoluteLifetime (asserted); a session whose last real request was 14 days ago but whose stamp is up to 59 minutes older is still recognised, and none survives past 14 d + 1 h of inactivity (Domain boundary tests); openapi examples show Max-Age.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
