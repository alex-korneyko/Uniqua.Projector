---
id: T27
title: "Move Api registrations into AddAccountsApi, require TrustedProxies outside Development, and prove a trusted proxy's forwarded address is honoured"
layer: "wiring"
deps: ["T24", "T26"]
acs: ["AC-01b"]
files_hint:
  - "src/Uniqua.Projector.Api/Program.cs"
  - "src/Uniqua.Projector.Api/DependencyInjection.cs"
  - "src/Uniqua.Projector.Api/appsettings.json"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RegisterEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-13, R-17, R-23"
status: "done"
---

# T27 — Move Api registrations into AddAccountsApi, require TrustedProxies outside Development, and prove a trusted proxy's forwarded address is honoured

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-13, R-17, R-23 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** T24, T26 (a shared file, see `files_hint`).

## Acceptance criteria

- AC-01b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] Program.cs names no inner-layer type (only AddXxx calls); outside Development the app refuses to start when TrustedProxies is empty; with TrustedProxies set to the test peer, two different X-Forwarded-For values get separate counters (tested).
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
