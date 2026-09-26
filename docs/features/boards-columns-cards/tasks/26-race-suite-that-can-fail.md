---
id: T26
title: "Make the simultaneous-change race suite able to fail, and add the add-card / delete-same-column pair"
layer: "tests"
deps: ["T25"]
acs: ["AC-03", "AC-09", "AC-10b", "AC-11", "AC-15"]
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Fixtures/ContentionForcer.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardInvariantRaceTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardStoreTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnUseCaseTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/CardUseCaseTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardUseCaseTests.cs"
owner: "Alex Korneiko"
estimate: "M"
status: "todo"
origin: "review 2026-09-26 — findings Q2a, Q2b (test side), Q6 (collision guard)"
---

# T26 — Make the simultaneous-change race suite able to fail, and add the add-card / delete-same-column pair

## Place in the sequence

- **Origin:** follow-up from the independent review, findings Q2a, Q2b (test side), Q6 (collision guard) — [review-2026-09-26.md](../_review/review-2026-09-26.md). The finding text there is the source; this brief restates it.
- **Blocked by:** T25

## What is wrong and what to do

- **Q2a** — `ContentionForcer` bumps the target row before every matching UPDATE while `Enabled` (`ContentionForcer.cs:116-128`), for both racers, so in the DeleteDelete, AddColumn, AddCard and CreateCreate pairs both racers exhaust their attempts and end `boards.contended`; `successes` is always 0 and the ceiling assertions cannot fail (`BoardInvariantRaceTests.cs:114-131,157-174,193-210,278-295`). The `forcedCollisionPairs > 0` guard (`:82-86`) is trivially satisfied. Give the forcer a budget (e.g. `BumpsRemaining = 1`, or bump only one racer's first attempt). In each pair assert **exactly one success and the loser's specific domain refusal** (LastColumn / ColumnLimitReached / CardLimitReached / OwnedBoardLimitReached); a `contended` result fails the test. Require a forced collision on every one-short-of-the-ceiling pair. Keep `BoardStoreTests.cs:172` (the unlimited-bump case) by making the budget configurable.
- **Q2b (test side)** — the add-card/delete-column pair always uses two different columns (`BoardInvariantRaceTests.cs:222-238`). Add the pair the NFR names: add-card-to-X racing delete-X, forced once, asserting the column is never deleted while holding a card and the loser gets `column_not_empty` (or the card add is refused because X is gone) — never a 500.
- **Natural-race use-case tests** (`ColumnUseCaseTests.cs:74,218`, `CardUseCaseTests.cs:58`, `BoardUseCaseTests.cs:88`) pass whether or not the tasks overlap. Drive them through the budgeted forcer so each proves the retry re-decides; rename `ColumnUseCaseTests.cs:218` if it stays unforced.

## Acceptance criteria

Re-read AC-03, AC-09, AC-10b, AC-11, AC-15 verbatim in [spec.md](../spec.md) §5 before writing the test; the test asserts the business-observable outcome the AC names.

**Fallback:** if this brief is insufficient or contradicted by the code, read the cited file:line, [spec.md](../spec.md), [sad.md](../sad.md), [contracts/openapi.yaml](../contracts/openapi.yaml), [screens.md](../screens.md) and [test-plan.md](../test-plan.md). Do not guess.

## Definition of Done

- [ ] Each race pair would fail if the retry replayed the stale decision instead of reloading (verify by temporarily breaking the retry locally — do not commit that). The suite asserts exactly-one-success + the loser's refusal for every pair, including the new add-card-to-X / delete-X pair. Integration suite green against the SQL Server container.
- [ ] Test written first and seen failing for the right reason (quote the failing line).
- [ ] Every CLAUDE.md rule still holds (layer direction, domain rules in Domain, ProblemDetails from the one handler, Tailwind only, permissive licences).
