---
id: T1
title: "Build the Board aggregate with the Text rule, board creation and the owner-only rules"
layer: "domain"
deps: []
blocks: ["T2", "T3", "T4", "T5"]
acs: ["AC-01", "AC-02", "AC-03", "AC-19", "AC-20", "AC-20b", "AC-22"]
files_hint:
  - "src/Uniqua.Projector.Domain/Boards/Board.cs"
  - "src/Uniqua.Projector.Domain/Boards/Column.cs"
  - "src/Uniqua.Projector.Domain/Boards/Card.cs"
  - "src/Uniqua.Projector.Domain/Boards/BoardMembership.cs"
  - "src/Uniqua.Projector.Domain/Boards/OwnedBoardCounter.cs"
  - "src/Uniqua.Projector.Domain/Boards/BoardText.cs"
  - "src/Uniqua.Projector.Domain/Boards/BoardError.cs"
  - "tests/Uniqua.Projector.Domain.Tests/Boards/BoardTextTests.cs"
  - "tests/Uniqua.Projector.Domain.Tests/Boards/BoardTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T1 — Build the Board aggregate with the Text rule, board creation and the owner-only rules

## Place in the sequence

- **Blocked by:** nothing — it starts in wave 1 · **Blocks:** T2 — Give the Board its column rules: add, rename, move and delete with dense positions and version checks, T3 — Admit, edit and delete cards through the Board's counters with the content-version check, T4 — Map the boards entities in EF Core and generate the CreateBoards migration, T5 — Declare the board ports and implement the member-scoped store with the bounded concurrency retry · **Wave:** 1 — it declares the entity shapes every backend task builds on, so it is the root of the backend branch; the client branch (T16–T18) starts beside it.
- **Lane:** shares `Board.cs`, `Column.cs`, `Card.cs` and `BoardError.cs` with T2 and T3 — serialized with them.

## Why (user story)

> **As an** account
> **I want** to create a board with a name and have it ready to use
> **So that** I can start organising work without setting anything up first
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

> **As a** board owner
> **I want** to rename the board, and to delete it with everything on it once I confirm
> **So that** the board's identity and existence stay under the control of the person who created it
>
> — `spec.md §4, US-06, verbatim` · full text: [spec.md](../spec.md)

This task makes a board exist in the domain — named by the Text rule, born with three columns and an `Owner` membership, capped at 50 per account — and puts renaming and deleting it behind the owner check.

## Inlined context

> ```
> src/Uniqua.Projector.Domain/Boards/
> ├── Board.cs                 # aggregate: name, columns, card counters, ColumnLayoutVersion;
> │                            # rename/delete (owner only), add/rename/move/delete column,
> │                            # admit a card to a column; every structural rule of spec §5
> ├── Column.cs                # name, dense position (ADR 0017), card count, next card position,
> │                            # NameVersion (ADR 0016)
> ├── Card.cs                  # title, description, gapped position, ContentVersion; edit/delete
> │                            # with the stale check
> ├── BoardMembership.cs       # (board, account, role Owner | Member) — ADR 0013
> ├── BoardText.cs             # the spec §5 Text rule: Unicode-whitespace trim, code-point length
> └── BoardError.cs            # not available, owner only, stale (with current state), limits
> ```
>
> — `sad.md §5, Internal decomposition (Domain), verbatim` · full text: [sad.md](../sad.md)

> **Text rule** (applies to every name, title and description limit below, and to §6 Content ceilings). Board names, column names and card titles are stored without the whitespace at their start and end, and their length is counted after that trimming; whitespace means every character Unicode classes as whitespace, including tabs and non-breaking spaces. A card description is stored exactly as typed, with nothing trimmed. A character is one Unicode code point, so a typical emoji counts as one, and the form and the system count the same way.
>
> — `spec.md §5, Text rule, verbatim` · full text: [spec.md](../spec.md)

> **Chosen:** Option 1 [membership records with a role — one record per (board, account) carrying `Owner` or `Member`; creating a board writes the creator's `Owner` record]. The membership check is one query whoever is asking, so an integration test that inserts a `Member` record exercises exactly the path production uses, and invitations only add records.
>
> — `adr/0013, Decision outcome, abridged` · full text: [0013](../adr/0013-grant-board-access-through-one-level-membership-records-with-an-owner-role.md)

> Board creation needs an owned-board counter per account that the domain owns; the account's Identity row is not the place (its `ConcurrencyStamp` belongs to Identity).
>
> — `adr/0015, Consequences (Negative), verbatim` · full text: [0015](../adr/0015-guard-board-invariants-with-an-optimistic-concurrency-token-and-bounded-retry.md)

> `App->>Domain: Check the owner, then compare the trimmed typed name with the current name, same letters and same case` — does not match (including a board renamed since the dialog opened) → refused, with the current name.
>
> — `sad.md §6, flow 11, delete branch, abridged` · full text: [sad.md](../sad.md)

> **Check precedence is now fixed by these flows**: not on this board → owner check (board rename and delete only) → text limits → stale → ceilings and column rules.
>
> — `sad.md §6, Flagged for the stages that follow, abridged` · full text: [sad.md](../sad.md)

> New boards get To do / In progress / Done at positions 0, 1, 2 (AC-01). The domain creates them; they are not seed data. [`ColumnLayoutVersion`, `NameVersion`, `ContentVersion` each start at 1.] Put the doubling in one place (for example `BoardText.StorageLength(limit)`), so that an `nvarchar` length never becomes a second statement of the rule.
>
> — `data-model.md §Entities + §Notes for implement, abridged` · full text: [data-model.md](../data-model.md)

> **Hard rule:** An invariant — "a card cannot move to a column of another board", "a board must keep at least one column" — is enforced by the entity itself, never by a use case and never by an endpoint. A use case orchestrates; it does not decide what is legal.
>
> — `CLAUDE.md §Domain rules live in Domain, verbatim` · full text: [CLAUDE.md](../../../../CLAUDE.md)

> **Hard rule:** New identifiers come from `Uniqua.Projector.Domain.Ids.New()`, which returns a GUID v7. […] The database never generates an id.
>
> — `CLAUDE.md §Ids are GUID version 7, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [data-model.md](../data-model.md) · [adr/](../adr/)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-01 — happy path

> **Given** an account signed in
> **When** they create a board and give it a name of 1 to 100 characters
> **Then** the system creates the board with that name, makes the account its board owner and only member, gives it three columns named To do, In progress and Done in that order, and opens it; a board name need not be unique, even among one account's boards
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-02 — error

> **Given** an account creating a board
> **When** they submit a name that is empty once surrounding spaces are removed, or longer than 100 characters
> **Then** the system refuses to create the board and tells them the name must be between 1 and 100 characters, leaving what they typed in place
>
> — `spec.md §5, AC-02, verbatim` · full text: [spec.md](../spec.md)

### AC-03 — domain invariant

> **Given** an account that already owns 50 boards
> **When** they try to create another
> **Then** the system refuses and tells them an account can own at most 50 boards, because each account's share of the product is bounded — and the limit holds even when several boards are created at the same moment
>
> — `spec.md §5, AC-03, verbatim` · full text: [spec.md](../spec.md)

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

## Checklist

- [ ] `BoardText` — trim every Unicode whitespace character (`char.IsWhiteSpace` over code points, not just ASCII) from names and titles, never from descriptions; length in code points (`StringInfo`/`Rune` enumeration, not `string.Length`); `StorageLength(limit) => 2 * limit` as the one place the `nvarchar` doubling lives — `Domain/Boards/BoardText.cs`.
- [ ] `BoardError` — the error cases this feature needs, as values `ProblemDetailsSetup` will map: `NotAvailable`, `OwnerOnly`, `BoardNameInvalid`, `OwnedBoardLimitReached`, `ConfirmationMismatch(currentName)` now; T2/T3 add theirs — `Domain/Boards/BoardError.cs`, following `Domain/Accounts/AccountError.cs`.
- [ ] Entity shapes with every persisted property T4 maps: `Board` (`Id`, `Name`, `CreatedAt`, `CardCount`, `ColumnLayoutVersion`, columns, memberships), `Column` (`Id`, `BoardId`, `Name`, `Position`, `CardCount`, `NextCardPosition`, `NameVersion`), `Card` (`Id`, `BoardId`, `ColumnId`, `Position`, `Title`, `Description`, `ContentVersion`), `BoardMembership` (`Id`, `BoardId`, `AccountId`, `Role`), `OwnedBoardCounter` (`AccountId`, `OwnedBoardCount`). Ids from `Ids.New()`; counters start at 1.
- [ ] `Board.Create(accountId, name, now)` returning `Result<Board, BoardError>` — Text rule on the name (1–100), three columns To do / In progress / Done at 0, 1, 2, one `Owner` membership.
- [ ] `OwnedBoardCounter.Admit()` / `Release()` — refuses the 51st owned board; `Release` on deletion.
- [ ] `Board.EnsureOwner(accountId)`, `Board.Rename(accountId, name)` (owner check first, then Text rule), `Board.ConfirmDeletion(accountId, typedName)` (owner check, then trimmed typed name compared ordinally with the current name, mismatch carries the current name).
- [ ] Unit tests — `tests/Uniqua.Projector.Domain.Tests/Boards/BoardTextTests.cs`, `BoardTests.cs`.

## Edge cases

| Case | Behaviour |
|---|---|
| Name of 100 emoji (200 UTF-16 units) | Accepted — 100 code points |
| Name of 101 code points after trimming | Refused `BoardNameInvalid` |
| Name that is only tabs and non-breaking spaces | Empty after trimming → refused `BoardNameInvalid` |
| Name with inner repeated spaces | Kept as typed inside; only the ends are trimmed |
| Two boards with the same name for one account | Both accepted (AC-01) |
| 50th board | Accepted; 51st refused `OwnedBoardLimitReached` |
| Delete confirmation `"  Q4 launch "` for board `Q4 launch` | Matches — the typed name is trimmed first |
| Delete confirmation `q4 launch` for `Q4 launch` | Mismatch — same letters, same case; carries the current name |
| A `Member` renames or deletes | `OwnerOnly`, before the name is even looked at |

## Definition of Done

- [ ] Domain unit tests pass for every row above and for each AC's domain decision.
- [ ] The Text rule, the 100-character limit and the 50-board ceiling each appear in exactly one place in Domain.
- [ ] Domain still references nothing (`Uniqua.Projector.Domain.csproj` has no new project or package reference).
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
