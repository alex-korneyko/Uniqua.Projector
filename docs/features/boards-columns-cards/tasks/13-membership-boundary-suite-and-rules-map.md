---
id: T13
title: "Prove the membership boundary across every read and change kind, and map the four board rules to their tests"
layer: "tests"
deps: ["T10", "T11", "T12", "T14"]
blocks: []
acs: ["AC-21", "AC-22", "AC-25", "AC-26", "AC-27"]
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardBoundaryTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/BoardRulesDocumentTests.cs"
  - "docs/board-rules.md"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T13 — Prove the membership boundary across every read and change kind, and map the four board rules to their tests

## Place in the sequence

- **Blocked by:** T10 — Expose the column endpoints: add, rename, move and delete, T11 — Expose the card endpoints: add, open, edit and delete, T12 — Limit each account to 120 board change attempts per rolling minute, before the membership check, T14 — Prove the board invariants under 1,000 simultaneous pairs and the column order under 1,000 random sequences · **Blocks:** nothing — it is a leaf · **Wave:** 8 — the backend's capstone; it waits on T14 only so the rules map can name the race tests.
- **Lane:** own lane.

## Why (user story)

> **As a** board owner
> **I want** any account that is not a member to be unable to see, change, or even confirm the existence of my board
> **So that** what I write on it is shared only with the people I chose
>
> — `spec.md §4, US-09, verbatim` · full text: [spec.md](../spec.md)

> **As a** board member who is not the board owner
> **I want** to change columns and cards exactly as the owner can, without being able to rename or delete the board
> **So that** I can collaborate fully without being able to take the board away from its owner
>
> — `spec.md §4, US-07, verbatim` · full text: [spec.md](../spec.md)

This task proves from outside, the way the reviewing engineer will probe it, that a non-member learns nothing and a non-owner member can do everything except take the board away.

## Inlined context

> | Indistinguishable refusal | 100% of read and change kinds, 0 differences: for each, the refusal for a board the caller is not a member of is identical, field for field, to the refusal for a board that does not exist | integration test comparing the two responses directly |
>
> — `spec.md §6, NFR row, verbatim` · full text: [spec.md](../spec.md)

> **How verify:** an integration test that, for every read and change kind, sends the same request to a board the caller is not a member of and to an identifier that never existed, and compares status, headers and body field for field — with invalid and stale bodies among the inputs, and cross-board column and card identifiers (AC-26); the non-owner cases run against a `Member` record inserted by test setup (ADR 0013).
>
> — `sad.md §10, QG-1, verbatim` · full text: [sad.md](../sad.md)

> The spec §6 "indistinguishable refusal" comparison excludes exactly those two [`instance`, `traceId`].
>
> — `contracts/openapi.yaml, components.responses.BoardNotAvailable, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

> **A reviewer can find the board's four rules and the test that proves each** — baseline: not answerable, nothing written; target: ≤ 2 minutes from opening the repository.
>
> — `spec.md §7, KPI 3, abridged` · full text: [spec.md](../spec.md)

> Four rules are fixed here: a non-member receives one identical refusal for any board they do not belong to, whatever else is wrong with their request, and that refusal never carries board content; every column and card a request names must belong to the board whose membership was checked; a column that still holds cards cannot be deleted, and a board always keeps at least one column; and a change made against an outdated view is refused rather than silently overwriting a newer change.
>
> — `spec.md §1, committed approach, verbatim` · full text: [spec.md](../spec.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) §10 · [openapi.yaml](../contracts/openapi.yaml)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

All 13 operations: `listMyBoards` (no board named — it must list no other board), `createBoard`, `openBoard`, `renameBoard`, `deleteBoard`, `addColumn`, `renameColumn`, `moveColumn`, `deleteColumn`, `addCard`, `openCard`, `editCard`, `deleteCard`. The one refusal: 404 `boards.not_available`; the owner-only refusal: 403 `boards.owner_only`, answered only to a member.

— `contracts/openapi.yaml, paths, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-21 — happy path

> **Given** a board member who is not its board owner
> **When** they add, rename, reorder or delete columns, or add, edit or delete cards
> **Then** the system accepts each change exactly as it would from the board owner, under the same rules
>
> — `spec.md §5, AC-21, verbatim` · full text: [spec.md](../spec.md)

### AC-22 — authorization

> **Given** a board member who is not its board owner
> **When** they try to rename the board or delete it
> **Then** the system refuses, leaves the board unchanged, and tells them only the board owner may rename or delete a board
>
> — `spec.md §5, AC-22, verbatim` · full text: [spec.md](../spec.md)

### AC-25 — authorization

> **Given** an account that is not a member of a board
> **When** they try to open it, or submit any change to it or to anything on it — including a change that is itself invalid or based on an outdated view
> **Then** the system gives them exactly the same refusal it gives for a board that does not exist, reveals nothing of the board's content, and changes nothing on any board
>
> — `spec.md §5, AC-25, verbatim` · full text: [spec.md](../spec.md)

### AC-26 — authorization

> **Given** a board member of one board, and any other board — whether or not they are also a member of it
> **When** they submit a change to the first board that names a column or a card belonging to the other board
> **Then** the system refuses it exactly as if that column or card did not exist, and changes nothing on either board
>
> — `spec.md §5, AC-26, verbatim` · full text: [spec.md](../spec.md)

### AC-27 — cross-context

> **Given** a visitor with no active session
> **When** they open a link to a board
> **Then** the system presents the sign-in form and reveals nothing about the board — not its name and not whether it exists — answering a link to a real board exactly as it answers one to a board that never existed; once they sign in, they are returned to that link's address and answered there as any signed-in account is — the board if they are a member, otherwise the refusal of AC-25
>
> — `spec.md §5, AC-27, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] A theory over the 12 board-naming operations × inputs {valid, invalid body, stale version, non-UUID id, cross-board column/card id}: non-member vs never-existed, compared on status, every header, and every body member except `instance` and `traceId` — `tests/.../Quality/BoardBoundaryTests.cs`.
- [ ] After each non-member request, assert nothing changed on any board.
- [ ] Cross-board: a member of boards A and B names B's column/card through A — `not_available`, nothing changed on either.
- [ ] Non-owner `Member` (`AMemberOfAsync`): every column and card change succeeds; rename and delete → `owner_only`, board unchanged.
- [ ] No session: every board-naming operation → the same 401 for a real and a never-existed id.
- [ ] `docs/board-rules.md` — the four rules of spec §1, each with the Domain type that enforces it and the test(s) that prove it (this suite and T14's); `BoardRulesDocumentTests.cs` asserts every named test exists, as `SessionRulesDocumentTests.cs` does.

## Edge cases

| Case | Behaviour |
|---|---|
| A non-member's stale `editCard` | 404 identical to never-existed — no `current_card` |
| A non-member's invalid `renameBoard` | 404 — not 400, not 403 |
| A board deleted a moment ago | 404 identical to never-existed (AC-20) |
| `Retry-After` / `Content-Length` headers | Must match too — body length cannot depend on the board |

## Definition of Done

- [ ] The suite passes and fails loudly if any single operation answers a non-member differently (verified once by temporarily breaking one use case).
- [ ] `docs/board-rules.md` exists and `BoardRulesDocumentTests` passes.
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
