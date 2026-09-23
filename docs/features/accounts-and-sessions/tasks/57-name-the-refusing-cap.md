---
id: T57
title: "Record which cap refused a sign-in, on the reservation and on the sign_in_rate_limited log line"
layer: "ports"
deps: ["T54"]
acs: ["AC-12"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/SignInRateLimit.cs"
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (third re-review) — S-04"
status: "done"
---

# T57 — Record which cap refused a sign-in, on the reservation and on the sign_in_rate_limited log line

## Place in the sequence

- **Origin:** a verdict from the independent third re-review — S-04 in [`_review/review-2026-09-23-2.md`](../_review/review-2026-09-23-2.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T54.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] SignInReservation.Refused carries which cap refused (per_address or per_source); the sign_in_rate_limited warning carries cap={Cap} alongside source, so sad §7's per-cap breakdown can be built from the logs; the 429 response body is unchanged (the cap is not disclosed to the caller); tests: a refusal by the per-pair cap logs cap=per_address and a refusal by the per-source ceiling logs cap=per_source.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
