---
id: T6
title: "Write the board use cases: create, list, open, rename and delete"
layer: "app"
deps: ["T5"]
blocks: ["T9"]
acs: ["AC-01", "AC-03", "AC-04", "AC-19", "AC-20", "AC-20b", "AC-22", "AC-25"]
files_hint:
  - "src/Uniqua.Projector.Application/Boards/CreateBoard.cs"
  - "src/Uniqua.Projector.Application/Boards/ListMyBoards.cs"
  - "src/Uniqua.Projector.Application/Boards/OpenBoard.cs"
  - "src/Uniqua.Projector.Application/Boards/RenameBoard.cs"
  - "src/Uniqua.Projector.Application/Boards/DeleteBoard.cs"
  - "src/Uniqua.Projector.Application/DependencyInjection.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardUseCaseTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T6 — Write the board use cases: create, list, open, rename and delete

## Place in the sequence

- **Blocked by:** T5 — Declare the board ports and implement the member-scoped store with the bounded concurrency retry · **Blocks:** T9 — Expose the board endpoints with lenient binding and map every board refusal to one problem document · **Wave:** 4.
- **Lane:** shares `Application/DependencyInjection.cs` with T7 and T8 — serialized with them.

## Why (user story)

> **As an** account
> **I want** to see every board I am a member of, and only those
> **So that** I can get back to my work and never stumble onto someone else's
>
> — `spec.md §4, US-02, verbatim` · full text: [spec.md](../spec.md)

> **As a** board owner
> **I want** to rename the board, and to delete it with everything on it once I confirm
> **So that** the board's identity and existence stay under the control of the person who created it
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

This task orchestrates the board-level actions — each one begins with the member-scoped load and asks the Board to decide.

## Inlined context

> **Chosen:** Option 1 [a member-scoped load in the use case] — every use case begins with a port call that returns the board only if the caller is a member; absent and not-a-member collapse into one application error; columns and cards are found through the loaded board; the owner rule is a Board method.
>
> — `adr/0014, Considered options + Decision outcome, abridged` · full text: [0014](../adr/0014-enforce-membership-by-loading-the-board-scoped-to-the-caller.md)

> Every use case begins with a port call that returns the board only if the caller is a member; "absent", "not a member", "deleted" and "a column or card not on this board" all become one `BoardNotAvailable` error that `ProblemDetailsSetup` maps to one refusal. […] Order per spec §6.1: session → per-account change limit → member-scoped load → validation, stale check, invariants.
>
> — `sad.md §4, strategic choice 4, abridged` · full text: [sad.md](../sad.md)

> Create: `App->>Infra: Read the account's owned-board counter` → `App->>Domain: Build the board - trims and checks the name, checks the 50-board ceiling` → `App->>Infra: Save the board, its columns, the membership and the incremented counter` guarded by the counter's concurrency token. Delete (matches): `Delete the board with its columns, cards and memberships, and free one owned-board slot` — in one transaction.
>
> — `sad.md §6, flows 1 + 11, abridged` · full text: [sad.md](../sad.md)

> **Use cases are plain classes returning `Result<T, TError>`** (`src/Uniqua.Projector.Domain/Result.cs`) — no mediator library; ports include `IUnitOfWork` and `IClock`.
>
> — `sad.md §2, Technical constraints, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule:** A use case orchestrates; it does not decide what is legal.
>
> — `CLAUDE.md §Domain rules live in Domain, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

> **Risk (High):** A board use case that skips the member-scoped load (ADR 0014) opens a hole in the boundary […] the `review` stage checks that every board use case begins with `LoadForMemberAsync`.
>
> — `sad.md §11, first row, abridged` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([sad.md](../sad.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes — writes `Boards`, `Columns`, `BoardMemberships`, `OwnedBoardCounters` (create), `Boards.Name` (rename), and deletes the board with its cascade plus `OwnedBoardCount - 1` (delete).

## API contract

Internal — no API surface; T9 exposes these as `listMyBoards`, `createBoard`, `openBoard`, `renameBoard`, `deleteBoard`. `OpenBoard` returns what `Board` carries: `id`, `name`, `is_owner`, `column_layout_version`, `columns[]`, `cards[]` (summaries without descriptions, ordered by column position then card position).

— `contracts/openapi.yaml, components.schemas.Board, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-01 — happy path

> **Given** an account signed in
> **When** they create a board and give it a name of 1 to 100 characters
> **Then** the system creates the board with that name, makes the account its board owner and only member, gives it three columns named To do, In progress and Done in that order, and opens it; a board name need not be unique, even among one account's boards
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-03 — domain invariant

> **Given** an account that already owns 50 boards
> **When** they try to create another
> **Then** the system refuses and tells them an account can own at most 50 boards, because each account's share of the product is bounded — and the limit holds even when several boards are created at the same moment
>
> — `spec.md §5, AC-03, verbatim` · full text: [spec.md](../spec.md)

### AC-04 — happy path

> **Given** an account that owns some boards and is a member of others
> **When** they open their list of boards
> **Then** the system lists every board they are a member of, most recently created first, marks the ones they own, and lists no other board
>
> — `spec.md §5, AC-04, verbatim` · full text: [spec.md](../spec.md)

### AC-19 — happy path

> **Given** the board owner viewing their board
> **When** they rename it to a name of 1 to 100 characters
> **Then** the system records the new name and every member sees it in their list of boards
>
> — `spec.md §5, AC-19, verbatim` · full text: [spec.md](../spec.md)

### AC-20 — happy path

> **Given** the board owner viewing their board
> **When** they choose to delete it and confirm by entering the board's current name exactly — the same letters in the same case, after the Text rule's trimming
> **Then** the system itself checks that confirmation, deletes the board together with all its columns and cards, and from then on answers any request about that board, or anything that was on it, exactly as it answers for a board that never existed
>
> — `spec.md §5, AC-20, verbatim` · full text: [spec.md](../spec.md)

### AC-20b — error

> **Given** the board owner deleting their board
> **When** the name they enter does not match the board's current name — including because the board was renamed since they opened it
> **Then** the system refuses, deletes nothing, and shows them the board's current name
>
> — `spec.md §5, AC-20b, verbatim` · full text: [spec.md](../spec.md)

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

## Checklist

- [ ] `CreateBoard` — counter → `Board.Create` → `OwnedBoardCounter.Admit` → save, inside `BoardChangeRetry` — `Application/Boards/CreateBoard.cs`.
- [ ] `ListMyBoards` — entries `{id, name, created_at, is_owner}` newest first — `ListMyBoards.cs`.
- [ ] `OpenBoard` — `LoadForMemberAsync` → null ⇒ `NotAvailable`; else columns + card summaries + `is_owner` — `OpenBoard.cs`.
- [ ] `RenameBoard` — load → `Board.Rename(accountId, name)` → save — `RenameBoard.cs`.
- [ ] `DeleteBoard` — load → `Board.ConfirmDeletion` → remove + `OwnedBoardCounter.Release` in one unit of work, inside the retry — `DeleteBoard.cs`.
- [ ] Register the five in `AddApplication` — `src/Uniqua.Projector.Application/DependencyInjection.cs`.
- [ ] Integration tests resolving the use cases from `ApiFactory` services, as `Accounts/RegisterAccountTests.cs` does — `tests/.../Boards/BoardUseCaseTests.cs`.

## Edge cases

| Case | Behaviour |
|---|---|
| Two creations at 49 owned boards, forced to collide | Exactly one succeeds, the other `OwnedBoardLimitReached` after the retry re-decides |
| Open a deleted board | `NotAvailable` — same value as never-existed |
| Rename by a `Member` with an invalid name | `OwnerOnly` — owner check before text limits |
| Delete a board that holds 3 cards | Board, columns, cards and memberships gone; counter decremented |
| List for an account with no memberships | Empty list, nothing written |

## Definition of Done

- [ ] Use-case integration tests pass for each AC above and each edge-case row.
- [ ] Every use case except `CreateBoard` and `ListMyBoards` begins with `LoadForMemberAsync` (asserted by review; the QG-1 suite in T13 proves it from outside).
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
