---
id: T53
title: "Trace the sign-in cap through sad, test-plan, session rules and README, fix the boundary rows, and retire the registration-only wording"
layer: "docs"
deps: ["T46", "T47", "T48", "T49"]
acs: ["AC-12", "AC-05b", "AC-07", "AC-07b"]
files_hint:
  - "docs/features/accounts-and-sessions/sad.md"
  - "docs/features/accounts-and-sessions/spec.md"
  - "docs/features/accounts-and-sessions/test-plan.md"
  - "docs/session-rules.md"
  - "README.md"
  - "src/Uniqua.Projector.Api/AccountProblems.cs"
  - "src/Uniqua.Projector.Api/ProblemDetailsSetup.cs"
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "src/Uniqua.Projector.Api/DependencyInjection.cs"
  - "docs/features/accounts-and-sessions/contracts/openapi.yaml"
  - "tests/Uniqua.Projector.Api.IntegrationTests/SessionRulesDocumentTests.cs"
  - "docs/features/accounts-and-sessions/tasks/tracker.md"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (second re-review) — P-03, P-04, Q-10, Q-15"
status: "done"
---

# T53 — Trace the sign-in cap through sad, test-plan, session rules and README, fix the boundary rows, and retire the registration-only wording

## Place in the sequence

- **Origin:** a verdict from the independent second re-review — P-03, P-04, Q-10, Q-15 in [`_review/review-2026-09-23.md`](../_review/review-2026-09-23.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T46, T47, T48, T49.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-05b — verbatim in [spec.md §5](../spec.md)
- AC-07 — verbatim in [spec.md §5](../spec.md)
- AC-07b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] sad §6 flow 4 gains the source-cap alt branch (the Api reserves before the App is called; 429 plus when to retry) and flow 6 shows record-then-hold; sad §7 monitoring lists sign-in cap refusals by source, and the §12 'request source' glossary and the accepted-debt line name both limits; docs/session-rules.md §4 and the README state the cap, its 429 and the accepted shared-source residual, and 'no lockout, ever' is corrected; SessionRulesDocumentTests checks the cap figures; test-plan.md gains rows for the cap (refused without verification, parallel and abandoned attempts, unregistered address, another address or source) and for the sign-in 429 screen state, and the boundary row reads 'one minute before 14 days + 1 hour -> live, at it -> dead; one minute before 90 days -> live, at it -> dead'; spec §6.1 records Q-10 (a wrong password for a registered address costs 2 more round trips than an unregistered one, accepted, with the measured cost); every stale 'registration only' line is corrected: spec.md:220, AccountProblems.cs comments, ProblemDetailsSetup.cs comment, RequestSource summary, the log value consequence=registration_limit_shared_by_all_callers -> rate_limits_shared_by_all_callers, and openapi's 'more than 20 failed' -> '20 failed sign-ins or attempts still in flight'; tracker.md's total is recounted.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
