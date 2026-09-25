---
id: T5
title: "Declare the board ports and implement the member-scoped store with the bounded concurrency retry"
layer: "infra"
deps: ["T1", "T4"]
blocks: ["T6", "T7", "T8"]
acs: ["AC-04", "AC-25", "AC-26"]
files_hint:
  - "src/Uniqua.Projector.Application/Boards/Ports/IBoardStore.cs"
  - "src/Uniqua.Projector.Application/Boards/Ports/IOwnedBoardCounterStore.cs"
  - "src/Uniqua.Projector.Application/Boards/BoardChangeRetry.cs"
  - "src/Uniqua.Projector.Infrastructure/Boards/BoardStore.cs"
  - "src/Uniqua.Projector.Infrastructure/Boards/OwnedBoardCounterStore.cs"
  - "src/Uniqua.Projector.Infrastructure/DependencyInjection.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Fixtures/ContentionForcer.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardStoreTests.cs"
owner: "Alex Korneiko"
estimate: "L"
context_budget: "M"
status: "todo"
---

# T5 — Declare the board ports and implement the member-scoped store with the bounded concurrency retry

## Place in the sequence

- **Blocked by:** T1 — Build the Board aggregate with the Text rule, board creation and the owner-only rules, T4 — Map the boards entities in EF Core and generate the CreateBoards migration · **Blocks:** T6 — Write the board use cases: create, list, open, rename and delete, T7 — Write the column use cases: add, rename, move and delete, with races re-decided, T8 — Write the card use cases: add, open, edit and delete through the board · **Wave:** 3 — the busiest backend node: all three use-case tasks wait on it.
- **Lane:** own lane. Per the contract-task rule it **declares** `IBoardStore` / `IOwnedBoardCounterStore` in Application and implements them in Infrastructure in the same change.

## Why (user story)

> **As a** board owner
> **I want** any account that is not a member to be unable to see, change, or even confirm the existence of my board
> **So that** what I write on it is shared only with the people I chose
>
> — `spec.md §4, US-09, verbatim` · full text: [spec.md](../spec.md)

This task makes the membership check a property of how a board is loaded: there is no port method that returns a board, a column or a card without passing it.

## Inlined context

> **The member-scoped load is a port method, the owner rule a domain method.** `IBoardStore.LoadForMemberAsync(boardId, accountId)` returns the board only for a member (ADR 0014); anything a request names on that board is looked up through it.
>
> — `sad.md §5, Building block view, verbatim` · full text: [sad.md](../sad.md)

> **Chosen:** Option 1 [optimistic concurrency token with bounded retry] — the board row carries a `rowversion`; every structural change also updates the board row; a conflicting writer reloads, re-runs the domain rules and retries up to 3 times. The 50-board cap uses the same pattern on a per-account owned-board counter. […] after 3 conflicts the change fails with a retryable problem rather than looping.
>
> — `adr/0015, Considered options + Consequences, abridged` · full text: [0015](../adr/0015-guard-board-invariants-with-an-optimistic-concurrency-token-and-bounded-retry.md)

> - **Board deletion must not let EF delete tracked columns before their cards.** […] Either delete the board with `ExecuteDeleteAsync` and let the store's cascade run, or delete `Cards` for the board first in the same transaction. The AC-20 integration test must delete a board that **holds cards**.
> - A concurrency exception on a column row during a *structural* change is therefore a retry under ADR 0015, not a stale refusal. Only a rename or delete of the column itself answers "renamed since you last saw it".
> - The `OwnedBoardCounters` row is inserted **lazily** […]. If two first creations race, […] the loser's primary-key violation is treated exactly like a token conflict: reload and re-decide.
> - `ContentionForcer` has to learn to match `UPDATE [Boards]`, `UPDATE [Columns]`, `UPDATE [Cards]` and `UPDATE [OwnedBoardCounters]`.
>
> — `data-model.md §Notes for implement + §OwnedBoardCounters, abridged` · full text: [data-model.md](../data-model.md)

> Member-scoped load: `BoardId = @b AND AccountId = @a` → `IX_BoardMemberships_BoardId_AccountId`. The board list: `AccountId = @a` → `IX_BoardMemberships_AccountId_BoardId` INCLUDE `Role`, then `PK_Boards` for `Name` and `CreatedAt`. One card through its board: `Id = @card AND BoardId = @board`. Card summaries: `IX_Cards_BoardId_ColumnId_Position` (covered, no description).
>
> — `data-model.md §Entities access patterns + §Indexes, abridged` · full text: [data-model.md](../data-model.md)

> **Hard rule:** `AppDbContext` lives in `src/Uniqua.Projector.Infrastructure/` and is reachable from nowhere else […]. Application declares the repository ports it needs; Infrastructure implements them.
>
> — `CLAUDE.md §Persistence is EF Core, verbatim` · full text: [CLAUDE.md](../../../../CLAUDE.md)

> **Hard rule (logging):** Board, column, card and account identifiers may be logged; **board names, column names, card titles and descriptions never are**.
>
> — `sad.md §8, Logging, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([sad.md](../sad.md) · [data-model.md](../data-model.md) · [adr/0014](../adr/0014-enforce-membership-by-loading-the-board-scoped-to-the-caller.md) · [adr/0015](../adr/0015-guard-board-invariants-with-an-optimistic-concurrency-token-and-bounded-retry.md)) and follow it. Do not guess.

## Data delta

No schema change — T4 created the tables. This task reads and writes all five; the concurrency-relevant columns:

| Column | Change |
|---|---|
| `Boards.RowVersion` | write condition on every structural change |
| `Columns.NameVersion` | write condition on a column rename / delete |
| `Cards.ContentVersion` | write condition on a card edit / delete |
| `OwnedBoardCounters.RowVersion` | write condition on board creation / deletion |

— `data-model.md §Entities, abridged` · full text: [data-model.md](../data-model.md)

## API contract

Internal — no API surface. The board list's cursor wrapper (`BoardListPage`) is shaped in T9; this port returns the entries newest first.

## Acceptance criteria

### AC-04 — happy path

> **Given** an account that owns some boards and is a member of others
> **When** they open their list of boards
> **Then** the system lists every board they are a member of, most recently created first, marks the ones they own, and lists no other board
>
> — `spec.md §5, AC-04, verbatim` · full text: [spec.md](../spec.md)

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

## Checklist

- [ ] `IBoardStore` — `LoadForMemberAsync(boardId, accountId)` (board + columns + the caller's role, or null), `ListForAccountAsync(accountId)`, `ReadCardSummariesAsync(boardId)`, `FindCardAsync(boardId, cardId)`, `Add`, `Remove`, `SaveAsync` — `Application/Boards/Ports/IBoardStore.cs`.
- [ ] `IOwnedBoardCounterStore` — load-or-create by account — `Application/Boards/Ports/IOwnedBoardCounterStore.cs`.
- [ ] A port-level `BoardConcurrencyConflict` signal (Application-defined), so no EF type crosses into Application.
- [ ] `BoardChangeRetry.RunAsync(attempt)` — re-runs the whole load → decide → save up to 3 times on a conflict, then returns a `Contended` outcome — `Application/Boards/BoardChangeRetry.cs`.
- [ ] `BoardStore` / `OwnedBoardCounterStore` over `AppDbContext`, member-scoped queries only, board deletion via `ExecuteDeleteAsync`, lazy counter insert with PK violation mapped to a conflict — `Infrastructure/Boards/`.
- [ ] Register both in `AddInfrastructure` — `src/Uniqua.Projector.Infrastructure/DependencyInjection.cs`.
- [ ] Generalise `ContentionForcer` to the four `UPDATE`s above — `tests/.../Fixtures/ContentionForcer.cs`.
- [ ] Integration tests — `tests/.../Boards/BoardStoreTests.cs`.

## Edge cases

| Case | Behaviour |
|---|---|
| Board id that is not a GUID | Never reaches the store as a GUID; treated as absent → null |
| Existing board, caller not a member | null — identical to an absent board |
| Card id of another board passed with this board's id | null (`Id = @card AND BoardId = @board`) |
| Conflict on 3 consecutive attempts | `Contended`; nothing written |
| Two first-ever creations for one account | Loser's PK violation → conflict → retried against the inserted row |
| Delete a board holding cards | One statement, cascades, no NO ACTION violation |

## Definition of Done

- [ ] Store integration tests pass for every row above against the SQL Server container.
- [ ] A test proves the member-scoped load issues one query joined to the caller's membership, and the list query touches only the caller's memberships.
- [ ] A forced collision via the generalised `ContentionForcer` is retried and re-decided (asserted by attempt count).
- [ ] No log line written by the store contains a name, title or description (`LogRecorder`).
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
