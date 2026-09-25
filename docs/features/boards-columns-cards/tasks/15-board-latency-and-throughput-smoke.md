---
id: T15
title: "Measure opening a full board, a single change and change throughput against the §6 budgets"
layer: "tests"
deps: ["T10", "T11", "T12"]
blocks: []
acs: []
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardLatencyBudgetTests.cs"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "S"
status: "todo"
---

# T15 — Measure opening a full board, a single change and change throughput against the §6 budgets

## Place in the sequence

- **Blocked by:** T10 — Expose the column endpoints: add, rename, move and delete, T11 — Expose the card endpoints: add, open, edit and delete, T12 — Limit each account to 120 board change attempts per rolling minute, before the membership check · **Blocks:** nothing — it is a leaf · **Wave:** 7 — needs the full API and the change limit, so the workload stays within 2 changes/s per account.
- **Lane:** own lane — runs beside T13 and T14.

## Why (user story)

> **As an** account
> **I want** to create a board with a name and have it ready to use
> **So that** I can start organising work without setting anything up first
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task keeps the thin path fast enough that opening a full board and making a change stay within the spec's budgets.

## Inlined context

> | Latency p95, opening a board holding 20 columns and 1,000 cards | ≤ 300 ms | server-side timing, sampled in the smoke run on the reference machine (workload: §8) |
> | Latency p95, a single change (add, rename, reorder, edit, delete) | ≤ 200 ms | server-side timing, sampled in the smoke run on the reference machine (workload: §8) |
> | Throughput | ≥ 50 changes/s across boards | smoke test on the reference machine […]; in CI a regression check only (workload and tolerance: §8) |
>
> — `spec.md §6, NFR rows, abridged` · full text: [spec.md](../spec.md)

> **Workload (closes spec §8's fourth open question, during design, with its default):** at least 25 accounts, each on its own board — the per-account limit caps one account at 2 changes/s — an even mix of the change kinds in spec §6, 60 s per run. In CI the same smoke run is a regression check only, failing on a p95 more than 25% slower than the last recorded run; the first runs set the baseline.
>
> — `sad.md §10, QG-3, verbatim` · full text: [sad.md](../sad.md)

> The `INCLUDE` [on `IX_Cards_BoardId_ColumnId_Position`] is what keeps the 300 ms p95 at the ceiling, because GUID v7 keys scatter one board's cards across the clustered index.
>
> — `data-model.md §Indexes, abridged` · full text: [data-model.md](../data-model.md)

**Fallback:** insufficient or contradicted by the code → read [sad.md](../sad.md) §10 and the existing `tests/Uniqua.Projector.Api.IntegrationTests/Quality/LatencyBudgetTests.cs` in full and follow them. Do not guess.

## Data delta

No DB changes.

## API contract

Times `openBoard` on a board seeded to 20 columns and 1,000 cards (`ABoardWithColumnsAsync` + `ABoardWithCardsAsync`), and an even mix of `addColumn`, `renameColumn`, `moveColumn`, `deleteColumn`, `addCard`, `editCard`, `deleteCard`, `renameBoard`.

— `contracts/openapi.yaml, paths, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

No acceptance criterion of its own — this task measures the spec §6 latency and throughput rows quoted above, which no §5 criterion states.

— `spec.md §6, abridged` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Server-side timing on opening a board and on every change (sad §8 Observability) — reuse the timing hook `LatencyBudgetTests.cs` already reads.
- [ ] Workload driver: ≥ 25 accounts, one board each, ≤ 2 changes/s per account, even mix, 60 s — `Quality/BoardLatencyBudgetTests.cs`.
- [ ] Assert absolute targets only when running on the reference machine (explicit opt-in switch); in CI compare to the last recorded baseline with a 25% tolerance, following the existing latency test's baseline mechanism.
- [ ] Mark the test so the ordinary `dotnet test` run can skip the 60 s smoke by trait, as the existing latency test does.

## Edge cases

| Case | Behaviour |
|---|---|
| No recorded baseline yet | The run records one and passes |
| A run hits `boards.change_rate_limited` | The workload is wrong — fail with a message naming the per-account rate |
| p95 over target on a developer machine | Reported, not failed — only the reference machine asserts absolute targets |

## Definition of Done

- [ ] The smoke run completes and records p95 open, p95 change and changes/s.
- [ ] The CI regression check passes against its baseline.
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
