---
id: T35
title: "Cap failed sign-ins per request source, counted before the verification, so hanging up no longer escapes the AC-12 delay"
layer: "ports"
deps: []
acs: ["AC-12", "AC-05b"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "src/Uniqua.Projector.Api/Accounts/SignInRateLimit.cs"
  - "src/Uniqua.Projector.Api/Accounts/RegistrationRateLimit.cs"
  - "src/Uniqua.Projector.Api/DependencyInjection.cs"
  - "src/Uniqua.Projector.Api/AccountProblems.cs"
  - "docs/features/accounts-and-sessions/contracts/openapi.yaml"
  - "docs/features/accounts-and-sessions/adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md"
  - "docs/features/accounts-and-sessions/spec.md"
  - "docs/features/accounts-and-sessions/sad.md"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/AccountProblemsTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 (re-review) — N-01"
status: "todo"
---

# T35 — Cap failed sign-ins per request source, counted before the verification, so hanging up no longer escapes the AC-12 delay

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent re-review — N-01 in [`_review/review-2026-09-22-2.md`](../_review/review-2026-09-22-2.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (shared files, see `files_hint`).

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-05b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] A failed-sign-in slot is reserved for the request source (RequestSource.Of, the trusted-proxy-resolved address registration already uses) BEFORE the password is verified and released only on a successful sign-in, so an abandoned or parallel attempt still counts; once a source holds the cap (default 20 failed sign-ins per 15-minute sliding window, the figure named as a constant and recorded in the amendment) the next attempt from it is refused WITHOUT verifying the password, with a new declared problem `accounts.sign_in_rate_limited` (429 + Retry-After) routed through the AccountProblems wording table; the cap applies identically to registered and unregistered addresses (AC-05b) and a correct password from a different source is still accepted immediately (AC-12); the dictionary is bounded and pruned like RegistrationRateLimit (extract a shared sliding-window limiter if that keeps both simpler); openapi.yaml declares the 429 on POST /api/v1/sessions; an ADR 0010 amendment records the decision, the figure and the residual risk (one account attacked from many sources), and spec §6.1 and the sad §8 "Guessing protection" row say the same; integration tests: the 21st failure from one source gets 429 without a verification, parallel hung-up attempts still consume slots, another source signs in with the correct password immediately, an unregistered address is capped identically.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
