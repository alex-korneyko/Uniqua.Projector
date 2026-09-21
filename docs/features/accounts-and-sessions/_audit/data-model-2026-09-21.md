---
status: Complete
owner: "Alex Korneiko"
updated_at: "2026-09-21"
feature_size: "M"
stage: data-model
---

# Audit — data-model · accounts-and-sessions · 2026-09-21

## Migrations are staged, not live

**Nothing was written into a live `migrations/` tree.** There is none yet — the repository holds no
source at this commit — and writing one here would drop a runnable, half-designed schema where a stray
`dotnet ef database update` could apply it before the feature exists.

| # | Staged file | What it does |
|---|---|---|
| 01 | `docs/features/accounts-and-sessions/migrations/01_create_identity_schema.{up,down}.sql` | The seven ASP.NET Core Identity tables at their defaults, plus `DisplayName`, `NormalizedDisplayName` and `LastFailedAttemptAt` on the user table; eight indexes, two of which this feature changes or adds |
| 02 | `…/02_create_sessions.{up,down}.sql` | The `Sessions` table of ADR 0008 and its three indexes |
| 03 | `…/03_create_data_protection_keys.{up,down}.sql` | The `DataProtectionKeys` table of ADR 0009 |

## Promote-time convention hint

The repository's migration convention (`architecture-map.md` §Migrations) is **EF Core migrations
generated from the model**, named `<timestamp>_<Name>.cs` under
`src/Uniqua.Projector.Infrastructure/Migrations/`. **There is no sequence number to reserve and therefore
no collision to avoid** — the timestamp is assigned by `dotnet ef migrations add` at promotion time, so
another feature promoting first cannot conflict with this one.

Consequently `implement` does **not** copy these `.sql` files anywhere. On each `layer: migration` task
it declares the model, runs `dotnet ef migrations add` in ordinal order (`CreateIdentitySchema` →
`CreateSessions` → `CreateDataProtectionKeys`), then runs `dotnet ef migrations script` and **diffs the
generated SQL against the staged file**. A difference is a finding to reconcile before applying, not
something to overwrite — that diff is the entire purpose of staging hand-written SQL in a repository
whose migrations are generated.

## Divergences — flagged, not silently applied

**1. ADR 0010 says a new column is not needed. It is.** ADR 0010's consequences record: «No new table and
no new column: the counter is already in the Identity schema.» That holds for the *count*
(`AccessFailedCount`) but not for the *clock*. AC-12 requires the failure count to return to zero «after
15 minutes in which no attempt is made at all», and sad §6 flow 6 fixes the mechanism: «The reset is
derived from the last-attempt time on the next read, not kept alive by a timer — so a restart cannot lose
it.» Identity stores no such timestamp. The schema therefore adds `LastFailedAttemptAt datetimeoffset NULL`
to `AspNetUsers` **[confirmed with the owner 2026-09-21]**, and Identity's own `LockoutEnd` is deliberately
left unused rather than repurposed — repurposing it would mean that anyone enabling the framework's
standard lockout would lock the owner out for 15 minutes, which is precisely the outcome ADR 0010 exists
to prevent. **Recommended follow-up: `/sdd:decide-adr` to amend ADR 0010's «no new column» consequence, or
a one-line correction in place.** This is a bookkeeping divergence, not a design change — the decision
itself (Identity's counter, lockout off, computed delay) is unchanged.

**2. `DataProtectionKeys.Id` is an `int IDENTITY`, not a version-7 GUID.** The architecture map fixes
`Guid.CreateVersion7()` as the ID strategy. This one table diverges because
`Microsoft.AspNetCore.DataProtection.EntityFrameworkCore` owns its shape and reads exactly three columns.
ADR 0009 anticipated this («key-ring rows are operational state inside an otherwise domain-shaped schema;
`data-model` keeps them clearly separated»), and the separation is kept: its own migration, its own
section, and no relationship to any domain table in the ER diagram.

**3. `EmailIndex` is made UNIQUE, where Identity's default is non-unique.** Required by AC-03. Filtered
`WHERE NormalizedEmail IS NOT NULL`, because the default column nullability is kept.

**4. Hand-written SQL in a generated-migration repository.** The staging discipline this stage applies and
the repository's «generated from the model» convention pull in opposite directions. Resolved in favour of
both: the SQL is staged as the *reviewed expected output*, and promotion is a generate-then-diff step
rather than a copy. Recorded here so the next reader does not mistake these files for migrations to run.

**5. Hosting divergence carried forward, untouched.** `architecture-map.md` still records hosting as
undecided while `sad.md` §7 assumes the self-hosted host. Nothing in this schema depends on the
difference — it is SQL Server either way — so the sad §11 row stands as written and no new risk is added.

## Decisions confirmed with the owner (greenfield — the repository had no convention to follow)

| Topic | Confirmed | Where it lands |
|---|---|---|
| Email normalisation | **In the application** — Identity's normalizer (trim + invariant upper-case), identical code path at registration and at sign-in; no column collation overridden | `NormalizedEmail` + `EmailIndex` |
| Display name | **A separate `DisplayName` column**, with `NormalizedDisplayName` carrying the unique index; Identity's `UserName` keeps the email address | `DisplayName`, `NormalizedDisplayName`, `DisplayNameIndex` |
| AC-12 failure clock | **A new `LastFailedAttemptAt` column**, rather than repurposing `LockoutEnd` | `AspNetUsers.LastFailedAttemptAt` |
| Identity schema breadth | **The full default seven tables**, not a trimmed set — five are expected to stay empty, and any framework capability can later be switched on in configuration without a migration | migration 01 |

Choices *not* put to the owner, because the architecture already fixed them: PK strategy
(`Guid.CreateVersion7()`, architecture-map §Conventions), the store (SQL Server via EF Core, ADR 0002),
and the session-record shape (ADR 0008).

## Spec / SAD edits made by this stage

- **spec.md §8, question 3 — closed.** «What normalisation applies to an email address at registration
  versus at sign-in» was due «before `/sdd:data-model`». It was answered by the owner during this run, in
  line with the standing default, and the checkbox now records the resolution and points at
  `data-model.md`.
- **sad.md §11 — the matching open-question row is struck through and marked Closed**, with the same
  resolution.

The other two spec §8 questions (how sign-out reaches an open live-update connection; whether
live-update traffic renews a session) are **due before roadmap step 8** and were left open. Neither
changes this schema: the first is a notification mechanism, and the second only decides *what writes*
`LastSeenAt`, not whether the column exists.

## Drift detection

**Not applicable this run — there is no domain layer to drift from.** The repository contains no source
at `783b53c`; `src/Uniqua.Projector.Domain/Accounts/` is a target path that `/sdd:scaffold` has yet to
create. No `_drift/` fixes were generated, and none could be.

Drift becomes checkable at the first `data-model --drift-only` run *after* the `Account` and `Session`
entities exist. At that point the pairs to check are: `Account.DisplayName` ↔ `AspNetUsers.DisplayName`
(both bounded at 50), `Account.LastFailedAttemptAt` ↔ the nullable column of the same name, and the
`Session` entity's four fields ↔ `Sessions`' four non-key columns — in particular that `RevokedAt` is
nullable on both sides, since `IsExpired(now)` and the revocation check depend on NULL meaning «live».

## Self-check (4 mandatory)

| Check | Result | Evidence |
|---|---|---|
| Naming matches the repository's convention | **PASS** | Identity table and column names are the framework defaults, unchanged; `Sessions` and its indexes follow EF Core's own naming (`IX_<table>_<column>`, `PK_<table>`, `FK_<table>_<principal>_<column>`), which is what `dotnet ef migrations add` will generate. Divergences are enumerated above, none silent |
| Down reversibility | **PASS** | Verified mechanically across all three pairs: 9 tables created / 9 dropped, 11 indexes created / 11 dropped, nothing missing. Every `.up.sql` has a `.down.sql` |
| FK indexes | **PASS** | 7 foreign keys. 5 carry an explicit index (`IX_Sessions_AccountId`, `IX_AspNetUserClaims_UserId`, `IX_AspNetUserLogins_UserId`, `IX_AspNetRoleClaims_RoleId`, `IX_AspNetUserRoles_RoleId`); 2 (`AspNetUserRoles.UserId`, `AspNetUserTokens.UserId`) are the leading column of their composite primary key and need no second index |
| Convention adherence | **PASS, with the five divergences above flagged** | No DB philosophy was imposed: no `CHECK` constraint, no trigger, no database `DEFAULT`, no soft-delete flag and no audit columns beyond the three the acceptance criteria actually read |

Additional checks run:

- **Mermaid** — the `erDiagram` was parsed with the real Mermaid parser (`mermaid.parse`, v11 via npm in
  the scratchpad): 1 block, **OK**. No `PK_FK` composite key class; every entity in a relationship is
  declared.
- **PII guard** — **PASS**. There are no seeds at all, so no address of any kind reaches a migration
  file. The test fixtures documented in `data-model.md` use `example.test` exclusively.
- **Index justification** — every one of the 11 indexes cites a concrete query from a sad §6 flow or a
  cascade path. Two candidates were considered and **discarded** for having no query: an index on
  `Sessions.LastSeenAt` (written on the hot path, never filtered on) and a `CreatedAt` audit column on
  the account (nothing reads it; the version-7 identifier already carries the instant).

## `<!-- TBD -->` items

**None.** Every column, type, constraint and index in `data-model.md` is decided and cited. The two
spec §8 questions still open do not touch the schema, as explained above.

## Breaking-change decompositions

**None required.** This is the repository's first schema: three pure `CREATE TABLE` migrations, no
`ALTER`, no rename, no drop, no backfill, and therefore no expand → backfill → contract sequence.

## Next stage

`/sdd:api accounts-and-sessions` — the schema changes, so `api`'s hard gate is satisfied and its
no-contract-change skip condition plainly does not hold: this feature adds register, sign-in and
sign-out endpoints.
