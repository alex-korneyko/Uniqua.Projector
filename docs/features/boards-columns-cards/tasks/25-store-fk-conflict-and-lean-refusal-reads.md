---
id: T25
title: "Retry a delete that loses to a card insert, read only what a refusal needs, and return the stored board name"
layer: "infra"
deps: ["T23", "T24"]
acs: ["AC-09", "AC-19"]
files_hint:
  - "src/Uniqua.Projector.Infrastructure/Boards/BoardStore.cs"
  - "src/Uniqua.Projector.Application/Boards/Ports/IBoardStore.cs"
  - "src/Uniqua.Projector.Application/Boards/BoardChangeRetry.cs"
  - "src/Uniqua.Projector.Application/Boards/OpenBoard.cs"
  - "src/Uniqua.Projector.Application/Boards/RenameBoard.cs"
  - "src/Uniqua.Projector.Application/Boards/DeleteBoard.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardEndpoints.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardEndpoints.Columns.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardStoreTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardUseCaseTests.cs"
owner: "Alex Korneiko"
estimate: "M"
status: "todo"
origin: "review 2026-09-26 — findings Q2b (code side), Q4f, Q4g, B6e (code comment)"
---

# T25 — Retry a delete that loses to a card insert, read only what a refusal needs, and return the stored board name

## Place in the sequence

- **Origin:** follow-up from the independent review, findings Q2b (code side), Q4f, Q4g, B6e (code comment) — [review-2026-09-26.md](../_review/review-2026-09-26.md). The finding text there is the source; this brief restates it.
- **Blocked by:** T23, T24

## What is wrong and what to do

- **Q2b** — `BoardStore.SaveAsync` (`BoardStore.cs:96`) catches only `DbUpdateConcurrencyException`. If a card insert into column X commits before a batched delete of X, the DELETE hits the NO ACTION FK from Cards (SQL error 547) and surfaces as a plain `DbUpdateException` → 500. Translate a `DbUpdateException` whose inner `SqlException.Number == 547` into `BoardConcurrencyConflict`, so the bounded retry reloads and the domain answers `boards.column_not_empty` (AC-09).
- **Q4f** — refusal paths (`BoardEndpoints.cs:231,249` `RefuseShapeAsync` + confirmation mismatch; `BoardEndpoints.Columns.cs:226` `WriteStaleAsync`) call `OpenBoard`, which also reads up to 1,000 card summaries (`OpenBoard.cs:25`). Only membership, the columns or the board name are needed. Give them a lighter read (or have the use case return the current state with the refusal).
- **Q4g** — the rename endpoint re-applies `BoardText.Trim` to build its response (`BoardEndpoints.cs:184-185`). Have `RenameBoard` return what `Board.Rename` stored, and the endpoint return that.
- **B6e (code side)** — `BoardChangeRetry.cs:34-35` XML doc says "at most 3 retries"; the code makes 3 attempts. Keep `MaxAttempts = 3` and correct the comment to "3 attempts (2 retries)". (The ADR 0015 wording is fixed separately in T33.)

## Acceptance criteria

Re-read AC-09, AC-19 verbatim in [spec.md](../spec.md) §5 before writing the test; the test asserts the business-observable outcome the AC names.

**Fallback:** if this brief is insufficient or contradicted by the code, read the cited file:line, [spec.md](../spec.md), [sad.md](../sad.md), [contracts/openapi.yaml](../contracts/openapi.yaml), [screens.md](../screens.md) and [test-plan.md](../test-plan.md). Do not guess.

## Definition of Done

- [ ] Integration tests (RED first) prove: a SaveAsync whose DELETE fails with FK 547 surfaces as `BoardConcurrencyConflict` and the use case re-decides to `column_not_empty` (drive it deterministically, e.g. insert a card on a separate connection between load and save); the rename response equals the stored name; refusal paths issue no card-summary query (use the existing `CommandRecorder` fixture). Build and format clean; existing tests green.
- [ ] Test written first and seen failing for the right reason (quote the failing line).
- [ ] Every CLAUDE.md rule still holds (layer direction, domain rules in Domain, ProblemDetails from the one handler, Tailwind only, permissive licences).
