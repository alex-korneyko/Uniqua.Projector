---
id: T28
title: "Close the integration coverage the test plan claims: AC-17 refusal counting and the missing rows"
layer: "tests"
deps: ["T23", "T25"]
acs: ["AC-02", "AC-06b", "AC-14", "AC-17", "AC-18", "AC-18b", "AC-19", "AC-20", "AC-21", "AC-23", "AC-25", "AC-28"]
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardChangeRateLimitTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/CardEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnUseCaseTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardUseCaseTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardBoundaryTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Fixtures/BoardFixtures.cs"
owner: "Alex Korneiko"
estimate: "L"
status: "todo"
origin: "review 2026-09-26 — findings B5 (test side), B7d, Q4h"
---

# T28 — Close the integration coverage the test plan claims: AC-17 refusal counting and the missing rows

## Place in the sequence

- **Origin:** follow-up from the independent review, findings B5 (test side), B7d, Q4h — [review-2026-09-26.md](../_review/review-2026-09-26.md). The finding text there is the source; this brief restates it.
- **Blocked by:** T23, T25

## What is wrong and what to do

- **B5** — the "a refused attempt is not counted" test (`BoardChangeRateLimitTests.cs:124-141`) cannot fail: under the frozen clock counted and refused attempts expire together. Rewrite: 120 counted at t0, advance ~30 s, send ≥120 refused attempts, advance ~31 s (the counted ones leave the minute, the refused would not have), assert the account is admitted. Also add a board the caller is not a member of to the identical-refusal set (`:65-106`).
- **B7d** — add the cases the test-plan rows claim but no test proves: AC-02 over-100 name + invalid owner rename + nothing written (`BoardEndpointTests.cs:46-56`); AC-14 invalid edit through the endpoint; AC-18/18b edit a deleted card, delete a deleted column, add a card to a deleted column, each refusal compared field-for-field with one for a random id (`CardEndpointTests.cs:305-317`, `ColumnUseCaseTests.cs:296-315`); AC-19 a fixture member's list shows the new name (`BoardUseCaseTests.cs:203-219`); AC-20 the former columns and cards are probed after the board is deleted (`BoardEndpointTests.cs:129-146`); AC-21 the member-level refusals (non-empty column, last column, limits, stale) run as a non-owner member (`BoardBoundaryTests.cs:219-281`); AC-25 no board content in logs (capture the logger and assert no board/column/card text appears across a refusal sweep); AC-28 session expiry driven by the clock, not only sign-out; AC-06b/AC-23 a genuine second client session (a second `HttpClient` signed in as another member).
- **Q4h** — `BoardBoundaryTests.cs:72-73` is a duplicate of `:70` labelled as a non-UUID case; make it send a real non-UUID board id or remove it.

## Acceptance criteria

Re-read AC-02, AC-06b, AC-14, AC-17, AC-18, AC-18b, AC-19, AC-20, AC-21, AC-23, AC-25, AC-28 verbatim in [spec.md](../spec.md) §5 before writing the test; the test asserts the business-observable outcome the AC names.

**Fallback:** if this brief is insufficient or contradicted by the code, read the cited file:line, [spec.md](../spec.md), [sad.md](../sad.md), [contracts/openapi.yaml](../contracts/openapi.yaml), [screens.md](../screens.md) and [test-plan.md](../test-plan.md). Do not guess.

## Definition of Done

- [ ] Every listed case is a test that fails when the behaviour breaks (check at least the AC-17 rewrite by breaking the counting locally — do not commit that). Integration suite green against the SQL Server container. Where a case genuinely cannot be built, stop and say why instead of narrowing silently.
- [ ] Test written first and seen failing for the right reason (quote the failing line).
- [ ] Every CLAUDE.md rule still holds (layer direction, domain rules in Domain, ProblemDetails from the one handler, Tailwind only, permissive licences).
