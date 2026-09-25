---
id: T4
title: "Map the boards entities in EF Core and generate the CreateBoards migration"
layer: "migration"
deps: ["T1"]
blocks: ["T5"]
acs: []
files_hint:
  - "docs/features/boards-columns-cards/migrations/01_create_boards.up.sql"
  - "docs/features/boards-columns-cards/migrations/01_create_boards.down.sql"
  - "src/Uniqua.Projector.Infrastructure/Boards/Configurations/"
  - "src/Uniqua.Projector.Infrastructure/AppDbContext.cs"
  - "src/Uniqua.Projector.Infrastructure/Migrations/"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardsSchemaTests.cs"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "todo"
---

# T4 — Map the boards entities in EF Core and generate the CreateBoards migration

## Place in the sequence

- **Blocked by:** T1 — Build the Board aggregate with the Text rule, board creation and the owner-only rules · **Blocks:** T5 — Declare the board ports and implement the member-scoped store with the bounded concurrency retry · **Wave:** 2 — runs beside T2/T3: it needs only T1's entity shapes.
- **Lane:** `layer: migration` — serialized by `implement`; own files otherwise.

## Why (user story)

> **As an** account
> **I want** to create a board with a name and have it ready to use
> **So that** I can start organising work without setting anything up first
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task gives the board, its columns, cards, memberships and the owned-board counter a place in the store — no ACs of its own; every later board task stands on it.

## Inlined context

> **The SQL is the review target, not the artifact that gets applied.** `implement` writes the EF Fluent configurations and runs `dotnet ef migrations add CreateBoards`, then checks that `dotnet ef migrations script <previous> CreateBoards` matches `01_create_boards.up.sql` in substance: same tables, types, nullability, keys, delete behaviours and index definitions.
>
> — `data-model.md §Staged migrations, verbatim` · full text: [data-model.md](../data-model.md)

> - **`Role`** is mapped as `HasConversion<string>().HasMaxLength(10)`. The filtered index is `.HasFilter("[Role] = N'Owner'")`, which must match the converted value exactly.
> - **Text columns** use `HasMaxLength(2 × limit)`, and `Description` has no max length. Put the doubling in one place (for example `BoardText.StorageLength(limit)`).
> - A column row carries `NameVersion` as its concurrency token […]. `Boards.RowVersion` and `OwnedBoardCounters.RowVersion` are `rowversion` tokens; `Cards.ContentVersion` is an EF concurrency token.
>
> — `data-model.md §Notes for implement + §Entities, abridged` · full text: [data-model.md](../data-model.md)

> Conventions: PascalCase plural table names; `PK_<Table>`, `FK_<Table>_<Principal>_<Column>`, `IX_<Table>_<Cols>`; primary keys `uniqueidentifier`, `ValueGeneratedNever`; no database `DEFAULT`; hard delete; **no `CHECK` constraints**.
>
> — `data-model.md §Conventions followed, abridged` · full text: [data-model.md](../data-model.md)

> **Hard rule:** Schema changes are EF Core migrations generated from the model, one per change, reviewed as SQL before they are applied. They run on startup in development and as an explicit step in deployment.
>
> — `CLAUDE.md §Persistence is EF Core, verbatim` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read [data-model.md](../data-model.md) and the staged SQL in full and follow them. Do not guess.

## Data delta

Staged pair: `docs/features/boards-columns-cards/migrations/01_create_boards.up.sql` / `01_create_boards.down.sql` — promoted as the EF migration `<timestamp>_CreateBoards`.

| Table | Columns (type) | Keys / delete behaviour | Indexes |
|---|---|---|---|
| `Boards` | `Id` uniqueidentifier · `Name` nvarchar(200) · `CreatedAt` datetimeoffset · `CardCount` int · `ColumnLayoutVersion` int · `RowVersion` rowversion | `PK_Boards` | — |
| `Columns` | `Id` · `BoardId` · `Name` nvarchar(100) · `Position` · `CardCount` · `NextCardPosition` · `NameVersion` (concurrency token) | FK → `Boards` CASCADE | `IX_Columns_BoardId_Position` (**not** unique) |
| `Cards` | `Id` · `BoardId` · `ColumnId` · `Position` · `Title` nvarchar(300) · `Description` nvarchar(max) · `ContentVersion` (concurrency token) | FK → `Boards` CASCADE; FK → `Columns` **NO ACTION** | `IX_Cards_BoardId_ColumnId_Position` INCLUDE `Title`, `ContentVersion`; `IX_Cards_ColumnId` |
| `BoardMemberships` | `Id` · `BoardId` · `AccountId` · `Role` nvarchar(10) | FK → `Boards` CASCADE; FK → `AspNetUsers` NO ACTION | UNIQUE `IX_…_BoardId_AccountId`; `IX_…_AccountId_BoardId` INCLUDE `Role`; UNIQUE `IX_…_BoardId_Owner` filtered `[Role] = N'Owner'` |
| `OwnedBoardCounters` | `AccountId` (PK, FK → `AspNetUsers` CASCADE) · `OwnedBoardCount` int · `RowVersion` rowversion | `PK_OwnedBoardCounters` | — |

— `data-model.md §Entities + §Indexes, abridged` · full text: [data-model.md](../data-model.md)

## API contract

Internal — no API surface.

## Acceptance criteria

This task carries no acceptance criterion of its own — it is the schema the board tasks read and write. Its test is structural: the generated script matches the staged SQL, and the migration applies and reverts.

— `data-model.md §Staged migrations, abridged` · full text: [data-model.md](../data-model.md)

## Checklist

- [ ] One `IEntityTypeConfiguration<T>` per entity — `Infrastructure/Boards/Configurations/{Board,Column,Card,BoardMembership,OwnedBoardCounter}Configuration.cs`, picked up by `ApplyConfigurationsFromAssembly`.
- [ ] `DbSet`s on `AppDbContext` — `src/Uniqua.Projector.Infrastructure/AppDbContext.cs`.
- [ ] `dotnet ef migrations add CreateBoards --project src/Uniqua.Projector.Infrastructure --startup-project src/Uniqua.Projector.Api --output-dir Migrations`.
- [ ] Diff `dotnet ef migrations script CreateDataProtectionKeys CreateBoards` against the staged up script; reconcile any difference in the configuration, never by editing the generated migration by hand.
- [ ] Schema test — `tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardsSchemaTests.cs`, following `Accounts/SessionSchemaTests.cs` and `Fixtures/SchemaQueries.cs`; extend `Accounts/MigrationRoundTripTests.cs`-style round trip to the new migration.

## Edge cases

| Case | Behaviour |
|---|---|
| Two `Owner` memberships on one board | Refused by the filtered unique index |
| Two memberships for the same (board, account) | Refused by `IX_BoardMemberships_BoardId_AccountId` |
| Deleting a `Columns` row that still has `Cards` | Refused by the NO ACTION FK (backs AC-09) |
| Deleting a `Boards` row holding columns and cards in one statement | Cascades to columns, cards and memberships |
| Two columns at the same `Position` mid-renumber | Allowed by the store — the index is deliberately not unique |

## Definition of Done

- [ ] Staged migration is promoted to live `migrations/`, then applies and reverts cleanly on the SQL Server container.
- [ ] Generated script matches `01_create_boards.up.sql` in substance (reviewed diff noted in the PR).
- [ ] Schema test asserts both unique indexes, the filtered owner index and the NO ACTION `Cards.ColumnId` FK.
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
