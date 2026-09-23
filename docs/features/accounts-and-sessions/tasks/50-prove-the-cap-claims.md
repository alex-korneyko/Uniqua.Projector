---
id: T50
title: "Prove the sign-in cap's claims end to end, and make the two tests that could not fail discriminating"
layer: "tests"
deps: ["T46", "T47"]
acs: ["AC-12", "AC-10"]
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RegisterEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionRecognitionTests.cs"
  - "src/Uniqua.Projector.Api/Accounts/SlidingWindowLimiter.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (second re-review) — Q-11, Q-12"
status: "done"
---

# T50 — Prove the sign-in cap's claims end to end, and make the two tests that could not fail discriminating

## Place in the sequence

- **Origin:** a verdict from the independent second re-review — Q-11, Q-12 in [`_review/review-2026-09-23.md`](../_review/review-2026-09-23.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T46, T47.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-10 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] Endpoint-level integration tests: 20 sign-ins whose delay is abandoned (TestClock.AbandonDelays) still hold their slots, so the 21st gets 429; more than 20 successful sign-ins from one source and address are never refused (fails if the success Release is removed); once capped, advancing the clock past SignInRateLimit.Window lets the next attempt reach verification; the release-a-refused-decision test fails when the !IsPermitted guard is removed; the store-outage test asserts 500 and a code other than accounts.session_not_recognised. Each new test is shown to fail against a deliberately broken variant before it is kept.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
