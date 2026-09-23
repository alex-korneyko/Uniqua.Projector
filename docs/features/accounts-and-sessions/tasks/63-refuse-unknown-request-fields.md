---
id: T63
title: "Refuse request bodies with unknown fields, as the contract's additionalProperties: false says"
layer: "ports"
deps: ["T62"]
acs: ["AC-05b"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RegisterEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (fourth re-review) — U-02"
status: "todo"
---

# T63 — Refuse request bodies with unknown fields, as the contract's additionalProperties: false says

## Place in the sequence

- **Origin:** a verdict from the independent fourth re-review — U-02 in [`_review/review-2026-09-23-3.md`](../_review/review-2026-09-23-3.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T62.

## Acceptance criteria

- AC-05b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] RegisterAccountRequest and CreateSessionRequest reject a JSON member they do not declare (JsonUnmappedMemberHandling.Disallow on each record, or the equivalent scoped to these two bodies), so a body with an extra field lands on the existing 400 accounts.request_malformed from the one ProblemDetails handler in every environment; on sign-in the refusal depends only on the body's shape, never on account state, so it adds no AC-05b oracle, and it happens before Reserve so it takes no rate-limit slot; the Account response schema keeps additionalProperties: false; tests: POST /api/v1/accounts and POST /api/v1/sessions with an otherwise valid body plus an extra "x": 1 member each answer 400 accounts.request_malformed, create no account or session, and (sign-in) take no slot; the existing valid-body tests stay green.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
