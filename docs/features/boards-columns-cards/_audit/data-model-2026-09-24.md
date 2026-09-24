# Audit — data-model · boards-columns-cards · 2026-09-24

**Size / route:** M / standard (from `.size` / `.route`). **Mode:** greenfield slice on a brownfield
repository: new tables only, and no existing table is altered.

## Staged migrations

The migrations are staged and are **not yet in the live `migrations/` tree**. `implement` promotes
them.

| Staged file | Promotes to |
|---|---|
| `docs/features/boards-columns-cards/migrations/01_create_boards.up.sql` | EF Core migration `<yyyyMMddHHmmss>_CreateBoards` in `src/Uniqua.Projector.Infrastructure/Migrations/` |
| `docs/features/boards-columns-cards/migrations/01_create_boards.down.sql` | that migration's `Down()` |

**Promote-time convention hint.** The repository uses EF Core timestamped migrations
(`yyyyMMddHHmmss_PascalName`), and the latest is `20260922074901_CreateDataProtectionKeys`.
`implement` assigns the real timestamp by running `dotnet ef migrations add CreateBoards --project
src/Uniqua.Projector.Infrastructure --startup-project src/Uniqua.Projector.Api --output-dir
Migrations` **after** writing the Fluent configurations. The staged SQL is the review target:
`implement` compares `dotnet ef migrations script 20260922074901_CreateDataProtectionKeys
CreateBoards` against it. The SQL files are never applied as they are.

## Conventions (derived, then corroborated)

| Topic | Source | Followed |
|---|---|---|
| Migration tool / naming | `architecture-map.md` `migration_tool: ef-core-migrations`; `CLAUDE.md`; live `Migrations/` | EF Core, timestamped, one per schema change |
| PK | `CLAUDE.md`, `Ids.cs`, `SessionConfiguration` | `uniqueidentifier`, GUID v7, `ValueGeneratedNever` |
| Naming | live migrations | PascalCase plural tables; `PK_`/`FK_`/`IX_` names |
| Audit columns | `Sessions` | Only where a rule or query reads one (`Boards.CreatedAt`, AC-04); no `UpdatedAt` |
| Delete | `Sessions` cascade; spec §3 | Hard delete |
| Constraints | `SessionConfiguration` (no `CHECK`), `ProjectorUserConfiguration` (unique indexes as facts) | No `CHECK`, no `DEFAULT`; uniqueness in the store |
| Concurrency | ADR 0015, ADR 0016 | `rowversion` on `Boards` and `OwnedBoardCounters`; `int` counters as EF tokens on `Columns` and `Cards` |

## Decisions taken in this stage (confirmed with the owner 2026-09-24)

1. **Plain FK `Cards.ColumnId`** → `Columns(Id)`, NO ACTION. The Board aggregate decides that a
   card's column is on the card's board (AC-26); a composite FK was declined.
2. **Filtered unique index: one `Owner` per board** (`IX_BoardMemberships_BoardId_Owner`).
3. **`Cards.Description` NOT NULL**, and an empty string means no description.

Taken without a question, because the architecture or the store's semantics settle them:

- **Text columns at 2 × the spec limit**, because code points are not UTF-16 units (see data-model.md).
- **ADR 0017 confirmed:** no unique index on (`BoardId`, `Position`) for columns.
- **Owned-board counter** is a separate table, `OwnedBoardCounters`, inserted lazily. A PK collision
  on the first creation is treated as a token conflict.
- **`BoardMemberships.AccountId` NO ACTION** toward `AspNetUsers`, whereas `Sessions` cascades.
  This is deliberate: a cascade would leave ownerless boards.

## Convention deviations (deliberate, flagged)

- **One staged pair, not one per entity.** The skill's greenfield default is one pair per entity,
  but the SAD (§5, §7) and `CLAUDE.md` ask for one migration per schema change, and the five
  tables are one new aggregate. The architecture wins.
- **Idempotence guards and an explicit transaction in the staged SQL** go beyond what EF generates.
  They are present only for hand review or hand application, and `implement` does not need to
  reproduce them.

## Breaking-change decompositions

None. No existing table is altered, so no expand → backfill → contract sequence is needed.

## Drift detection

**No drift possible.** No `Boards` domain layer exists yet (`src/Uniqua.Projector.Domain/` holds
only `Accounts/`, `Ids.cs` and `Result.cs`), so there is nothing to map field by field. No `_drift/`
files.

**Observation outside this feature:** `ProjectorUserConfiguration` sizes `DisplayName` at
`HasMaxLength(50)`. If accounts-and-sessions counts a display name in code points, as this
feature's Text rule does, a 50-emoji display name would pass the domain and be refused by the store.
The accounts-and-sessions spec states no counting rule. This is worth a look in a `/sdd:fix` or a
spec clarification there; it is not changed here.

## Risks handed to `implement`

- **Board deletion and EF tracked cascades:** EF must not delete tracked columns before their cards
  (see data-model.md, Notes for `implement`). The AC-20 test must delete a board that holds cards.
- **SQL Server diamond cascade:** Boards → Columns (CASCADE), Boards → Cards (CASCADE), and
  Columns → Cards (NO ACTION). SQL Server checks the NO ACTION reference after the cascades of the
  single `DELETE Boards` statement. The same AC-20 test proves this on the real store; if it fails,
  `BoardStore` deletes the board's cards explicitly first.
- **Column-row conflicts during card add or delete** are a retry, not a stale refusal.

## `<!-- TBD -->`

- `BoardMemberships.AccountId` delete behaviour is left for the first feature that deletes accounts.

## Self-check (4 mandatory)

| Check | Result |
|---|---|
| Naming matches the repository | ✅ PascalCase plural tables, `PK_`/`FK_`/`IX_` names as in `CreateSessions` |
| Down reversibility | ✅ 5 `CREATE TABLE` ↔ 5 `DROP TABLE` in reverse FK order; the indexes and FKs drop with their tables |
| FK indexes | ✅ `Columns.BoardId` → `IX_Columns_BoardId_Position`; `Cards.BoardId` → `IX_Cards_BoardId_ColumnId_Position`; `Cards.ColumnId` → `IX_Cards_ColumnId`; `BoardMemberships.BoardId` → `IX_BoardMemberships_BoardId_AccountId`; `BoardMemberships.AccountId` → `IX_BoardMemberships_AccountId_BoardId`; `OwnedBoardCounters.AccountId` → PK |
| Convention adherence | ✅ Two deviations, both flagged above |
| ER diagram parses | ✅ `@mermaid-js/mermaid-cli` render, exit 0 |
| PII in seeds | ✅ No seeds; the fixtures use `example.test` |

**Next stage:** `/sdd:api boards-columns-cards`.
