---
id: T24
title: "Keep board content out of domain refusal details"
layer: "domain"
deps: []
acs: ["AC-25"]
files_hint:
  - "src/Uniqua.Projector.Domain/Boards/BoardError.cs"
  - "tests/Uniqua.Projector.Domain.Tests/Boards/BoardTests.cs"
  - "tests/Uniqua.Projector.Domain.Tests/Boards/BoardColumnTests.cs"
  - "tests/Uniqua.Projector.Domain.Tests/Boards/CardTests.cs"
owner: "Alex Korneiko"
estimate: "S"
status: "todo"
origin: "review 2026-09-26 — findings Q4e"
---

# T24 — Keep board content out of domain refusal details

## Place in the sequence

- **Origin:** follow-up from the independent review, findings Q4e — [review-2026-09-26.md](../_review/review-2026-09-26.md). The finding text there is the source; this brief restates it.
- **Blocked by:** —

## What is wrong and what to do

- **Q4e** — `ConfirmationMismatch`, `ColumnRenamed`, `ColumnsChanged` and `CardChanged` build `BoardError.Detail` from the board name, column names or card title (`BoardError.cs:53-55,71-73,89-92,118-120`). sad.md §8 Logging forbids board content in any detail. Nothing emits it today, but one log call would leak it. Use fixed sentences; if a caller needs current state, carry it as typed properties on the error, never in prose.

## Acceptance criteria

Re-read AC-25 verbatim in [spec.md](../spec.md) §5 before writing the test; the test asserts the business-observable outcome the AC names.

**Fallback:** if this brief is insufficient or contradicted by the code, read the cited file:line, [spec.md](../spec.md), [sad.md](../sad.md), [contracts/openapi.yaml](../contracts/openapi.yaml), [screens.md](../screens.md) and [test-plan.md](../test-plan.md). Do not guess.

## Definition of Done

- [ ] Domain unit tests (RED first) assert that no BoardError produced by any refusal contains the board name, a column name, a card title or a card description in `Detail`, and that any current state callers need is available as typed properties. Existing domain tests stay green.
- [ ] Test written first and seen failing for the right reason (quote the failing line).
- [ ] Every CLAUDE.md rule still holds (layer direction, domain rules in Domain, ProblemDetails from the one handler, Tailwind only, permissive licences).
