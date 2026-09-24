---
status: Draft
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-24"
feature_size: "M"
---

# API sync report — boards-columns-cards

Companion to `contracts/openapi.yaml`. The contract is a **derived** artifact: `data-model.md`
(typed shape) + `sad.md` §6 sequences (error branches) + `spec.md` §4/§5 (endpoint list, observable
outcomes) → OpenAPI. This report is the evidence that the derivation held.

**Inputs read.** `data-model.md` ✓ (present, so the default path applies and there is no fast-lane
skip) · `sad.md` §6 ✓ (thirteen flows) · `sad.md` §7, §8, §11 ✓ · `spec.md` §4/§5/§6/§6.1 ✓ · ADRs
0012–0018 ✓ (0014, 0016, 0018 shape the wire) · `ux-flows.md` SCR inventory ✓ · the
accounts-and-sessions contract ✓ (house conventions) · `src/Uniqua.Projector.Api/AccountProblems.cs`
and `ProblemDetailsSetup.cs` ✓ (the error registry) · `.size` = `M` · `.route` = `standard`.

**Interface kind.** `sad.md` frontmatter `target_surfaces: [backend-service, web-frontend]`, **read,
not re-derived**. The `backend-service` sub-kind is HTTP/REST, so the output is
`contracts/openapi.yaml`. `web-frontend` *consumes* this contract and authors none of its own.

**`contracts/events.md` is N/A, deliberately.** sad.md §6 flags: "No flow is asynchronous — there
is no queue, callback or scheduled job in this feature." The live-update hub (ADR 0004) arrives at
roadmap step 8. That is where the board events will be contracted.

**Idempotency-Key is N/A.** No §6 flow shows a client retry or an async actor on a request path. ADR
0015's bounded retry is server-internal, inside one request. Creating a board or a card twice is two
changes, each counted and each visible. No operation is marked key-required.

**Decisions taken with the owner this run (2026-09-24)**, each recorded where it applies below:

1. Version counters and the board-deletion confirmation travel in the **JSON body** on every change,
   DELETE included (ADR 0016's open wire question).
2. The board list keeps the **cursor wrapper** with an opaque cursor.
3. Retry exhaustion under ADR 0015 answers **503 `boards.contended`**. The sequence gap goes to
   `sequences` (OQ-API-1).
4. An out-of-range column position and a JSON body of the wrong shape get their own codes. Both gaps
   go to `sequences` (OQ-API-2, OQ-API-3).

---

## Section A — field origins

One row per `(operation, field)`. Shared schemas are listed once, and every operation that returns
one inherits its rows through `$ref`.

### Requests

| schema_path | origin | confidence |
|---|---|---|
| `createBoard.request.name` | data-model.md → `Boards.Name` `nvarchar(200)`, 1–100 code points after trim (AC-01, AC-02) | high |
| `renameBoard.request.name` | data-model.md → `Boards.Name` (AC-19) | high |
| `deleteBoard.request.confirm_name` | spec AC-20 / AC-20b, compared to `Boards.Name`. Request-only, no column | medium |
| `addColumn.request.name` | data-model.md → `Columns.Name` `nvarchar(100)`, 1–50 after trim (AC-05, AC-08) | high |
| `renameColumn.request.name` | data-model.md → `Columns.Name` (AC-06) | high |
| `renameColumn.request.name_version` | data-model.md → `Columns.NameVersion` int, starts at 1 (ADR 0016) | high |
| `deleteColumn.request.name_version` | data-model.md → `Columns.NameVersion` (AC-06b, flow 8) | high |
| `moveColumn.request.position` | data-model.md → `Columns.Position`, dense 0..n-1, n ≤ 20 (ADR 0017) | high |
| `moveColumn.request.column_layout_version` | data-model.md → `Boards.ColumnLayoutVersion` int, starts at 1 (ADR 0016) | high |
| `addCard.request.column_id` | data-model.md → `Cards.ColumnId` FK → `Columns.Id` | high |
| `addCard.request.title` | data-model.md → `Cards.Title` `nvarchar(300)`, 1–150 after trim (AC-12, AC-14) | high |
| `addCard.request.description` | data-model.md → `Cards.Description` `nvarchar(max)`, ≤ 10,000, as typed, "" = none | high |
| `editCard.request.title` / `.description` | as `addCard` (AC-13, AC-14) | high |
| `editCard.request.content_version` | data-model.md → `Cards.ContentVersion` int, starts at 1 (ADR 0016) | high |
| `deleteCard.request.content_version` | data-model.md → `Cards.ContentVersion` (AC-23, flow 10) | high |
| `listMyBoards.after` / `.before` / `.limit` | derived (cursor wrapper convention) | high |
| `*.X-XSRF-TOKEN` | sad.md §8 Cross-site request forgery. `maxLength: 512` inherited from the accounts contract, where it is an inference | low |
| `*.boardId` / `.columnId` / `.cardId` | data-model.md → `Boards.Id` / `Columns.Id` / `Cards.Id`. Declared `type: string` and not `format: uuid`, per ADR 0014 lenient binding | high |

### Responses

| schema_path | origin | confidence |
|---|---|---|
| `BoardListEntry.id` / `.name` | data-model.md → `Boards.Id`, `Boards.Name` | high |
| `BoardListEntry.created_at` | data-model.md → `Boards.CreatedAt` `datetimeoffset` (AC-04 ordering) | high |
| `BoardListEntry.is_owner` | data-model.md → the caller's `BoardMemberships.Role = Owner` (ADR 0013) | high |
| `BoardListPage.items/has_next/has_prev/next_cursor/prev_cursor` | derived (cursor wrapper convention). `prev_cursor` is added so `before` has a value to take | high |
| `Board.id` / `.name` | data-model.md → `Boards` | high |
| `Board.is_owner` | data-model.md → `BoardMemberships.Role` (flow 5: "with whether they own it") | high |
| `Board.column_layout_version` | data-model.md → `Boards.ColumnLayoutVersion` | high |
| `Board.columns[]` → `Column` | data-model.md → `Columns` (flow 5, ADR 0018) | high |
| `Board.cards[]` → `CardSummary` | data-model.md → `Cards`, covered by `IX_Cards_BoardId_ColumnId_Position` INCLUDE `Title`, `ContentVersion` (ADR 0018) | high |
| `Column.id/name/position/name_version` | data-model.md → `Columns.Id/Name/Position/NameVersion` | high |
| `ColumnLayout.column_layout_version` / `.columns` | data-model.md → `Boards.ColumnLayoutVersion` + `Columns` (flows 7, 8: "the new order and layout version") | high |
| `AddedColumn.column` / `.column_layout_version` | flow 6 add: "The column and the new layout version" | high |
| `CardSummary.id/column_id/position/title/content_version` | data-model.md → `Cards.*` (ADR 0018's summary list, verbatim) | high |
| `Card.description` | data-model.md → `Cards.Description` | high |
| `BoardName.id/name` | flow 11 rename: "The new name" | high |
| `Problem.type/title/status/detail/instance/code` | CLAUDE.md + sad.md §8 (RFC 9457), accounts precedent | high |
| `Problem.traceId` | `ProblemDetailsSetup.cs` emits it on every problem. It was undeclared in the accounts contract and is declared here so F-2 can name it | high |
| `Problem.retry_after_seconds` | spec AC-17 ("tells them when they may continue"), accounts precedent | high |
| `Problem.current_card` | spec AC-23 ("shows them the card as it is now") + flow 2/10 stale branches → `Card` | high |
| `Problem.current_column` | spec AC-06b ("shows its current name") + flows 6/8 → `Column` (carries `name_version` for the retry) | high |
| `Problem.current_layout` | spec AC-24 ("shows the current columns and order") + flow 7 → `ColumnLayout` | high |
| `Problem.current_name` | spec AC-20b ("shows them the board's current name") + flow 11 → `Boards.Name` | high |

**Deliberately absent from every response**, so that the absence reads as a decision:
`Boards.RowVersion` and `OwnedBoardCounters.RowVersion` (ADR 0015 says they are internal and never
sent), `Boards.CardCount` / `Columns.CardCount` / `Columns.NextCardPosition` (the Board aggregate's
bookkeeping, which the client derives from `cards[]` where it needs to), `OwnedBoardCounters.OwnedBoardCount`
(no AC shows the count), `BoardMemberships.Id`, and any other member's identity. No operation lists
members, and until step 7 there are no other members to list.

---

## Section B — drift findings

### Forward — is the contract derived correctly?

**1. Endpoint ↔ data-model** *(core)*: **✓**

| operation | entity read / written |
|---|---|
| `listMyBoards` | reads `BoardMemberships` by account (`IX_BoardMemberships_AccountId_BoardId`) → `Boards` |
| `createBoard` | reads/writes `OwnedBoardCounters`; writes `Boards`, 3 × `Columns`, `BoardMemberships` (Owner) |
| `openBoard` | reads `BoardMemberships` (member-scoped) → `Boards`, `Columns`, `Cards` summaries |
| `renameBoard` | writes `Boards.Name` (under `RowVersion`) |
| `deleteBoard` | deletes `Boards` (cascades `Columns`, `Cards`, `BoardMemberships`); writes `OwnedBoardCounters` |
| `addColumn` | writes `Columns`; `Boards.ColumnLayoutVersion` + `RowVersion` |
| `renameColumn` | writes `Columns.Name`, `Columns.NameVersion` (write condition) |
| `moveColumn` | writes every `Columns.Position`; `Boards.ColumnLayoutVersion` |
| `deleteColumn` | deletes `Columns`; renumbers `Columns.Position`; `Boards.ColumnLayoutVersion` |
| `addCard` | writes `Cards`; `Boards.CardCount`, `Columns.CardCount`, `Columns.NextCardPosition` |
| `openCard` | reads `Cards` through the board (`PK_Cards` + `BoardId`) |
| `editCard` | writes `Cards.Title/Description/ContentVersion` (write condition) |
| `deleteCard` | deletes `Cards`; `Boards.CardCount`, `Columns.CardCount` |

Every endpoint touches at least one entity, and every entity is reachable. The 13 operations are
exactly the 13 use cases of sad.md §5 (`CreateBoard` … `DeleteCard`).

**2. Error code ↔ repo error definition** *(core)*: **✓ (partly recorded as a proposal, not failed)**

The repository's form is a **wording table**: `src/Uniqua.Projector.Api/AccountProblems.cs` maps each
code to a status, a type URI and a title, and Domain sentinels (`AccountErrors`) own the sentences.
Checked in that form:

- **Existing, reused verbatim:** `accounts.session_not_recognised` (401), `accounts.antiforgery_failed`
  (403), `api.unexpected` (500), `api.request_too_large` (413), `api.request_rejected` (4xx family).
  All five are present in `AccountProblems.cs` and match their status and type URI.
- **No board registry exists yet.** The 20 `boards.*` codes below are this contract's proposal.
  sad.md §5 already names their home, `src/Uniqua.Projector.Api/Boards/BoardProblems.cs`, with the
  sentences in `Domain/Boards/BoardError.cs`. `implement` creates both from this table, and
  `--reconcile` checks them back. Each type URI follows `AccountProblems`' rule
  (`problems/` + code with `.`→`/`, `_`→`-`).

| code | status | criterion | §6 branch | before / after membership |
|---|---|---|---|---|
| `boards.not_available` | 404 | AC-18, AC-18b, AC-20, AC-25, AC-26 | flows 2, 5–10 not-on-this-board / not-a-member | the membership answer |
| `boards.change_rate_limited` | 429 | AC-17 | flow 2, first | before |
| `boards.request_malformed` | 400 | *none* (sad §8 Authorization, ADR 0014) | *none* → **OQ-API-3** | before |
| `boards.request_invalid` | 400 | *none* (ADR 0014 lenient binding) | *none* → **OQ-API-3** | after |
| `boards.board_name_invalid` | 400 | AC-02 | flow 1 first; flow 11 rename first | after |
| `boards.column_name_invalid` | 400 | AC-08 | flow 6, add + rename | after |
| `boards.card_title_invalid` | 400 | AC-14 | flows 2, 9 | after |
| `boards.card_description_invalid` | 400 | AC-14 | flows 2, 9 | after |
| `boards.column_position_invalid` | 400 | *none* | *none* → **OQ-API-2** | after |
| `boards.owner_only` | 403 | AC-22 | flow 11, non-owner | after |
| `boards.owned_board_limit_reached` | 409 | AC-03 | flow 1, first | n/a (no board named) |
| `boards.column_limit_reached` | 409 | AC-11 | flow 6, add ceiling | after |
| `boards.card_limit_reached` | 409 | AC-15 | flow 9, ceiling | after |
| `boards.column_not_empty` | 409 | AC-09 | flow 8, holds cards | after |
| `boards.last_column` | 409 | AC-10, AC-10b | flow 8 only column; flow 3 | after |
| `boards.column_renamed` | 409 | AC-06b | flow 6 rename stale; flow 8 stale | after |
| `boards.columns_changed` | 409 | AC-24 | flow 7, stale | after |
| `boards.card_changed` | 409 | AC-23 | flow 2 stale; flow 10 stale | after |
| `boards.confirmation_mismatch` | 409 | AC-20b | flow 11, does-not-match | after |
| `boards.contended` | 503 | *none* (ADR 0015, sad §7) | *none* → **OQ-API-1** | after |

All codes match `^[a-z_]+\.[a-z_]+$`. The `boards.` prefix is also the `module=boards` log tag of
sad.md §8, and it matches the `boards.change_rate_limited` metric sad.md §7 already names.

**Implementation note for the registry.** `ProblemDetailsSetup.IsAccountsOrSessionsRequest` reshapes
a framework 400/415 into `accounts.request_malformed` on accounts and sessions paths only. A
non-JSON body on `/api/v1/boards/**` would currently fall into `api.request_rejected`. `implement` must
add the `boards.request_malformed` mapping for that prefix, in the setup's `Development` exception
path as well, the same way `accounts.request_malformed` is handled.

**3. Validation ↔ constraint** *(core)*: **✓**

| contract | data-model / spec | verdict |
|---|---|---|
| `BoardNameText` 1..100 | `Boards.Name nvarchar(200)` = 2 × 100 code points; `BoardText` enforces 100 | exact: JSON Schema `maxLength` counts code points, as the Text rule does |
| `ColumnNameText` 1..50 | `Columns.Name nvarchar(100)` | exact |
| `CardTitleText` 1..150 | `Cards.Title nvarchar(300)` | exact |
| `CardDescriptionText` ≤ 10000, min 0 | `Cards.Description nvarchar(max)`, "" = none, never NULL | exact: `type: string`, not nullable |
| request `description maxLength: 10000` | not trimmed, so the raw length is the counted length | exact |
| `Column.position` 0..19; `MoveColumnRequest.position` 0..19 | dense 0..n-1, n ≤ 20 (AC-11, ADR 0017) | exact; the tighter 0..n-1 is state-dependent → `boards.column_position_invalid` |
| `CardSummary.position` ≥ 0, no max | gapped, ascending, `NextCardPosition` never reused (ADR 0017) | exact: deliberately not an index |
| `*_version` ≥ 1 | all three counters "Start at 1" | exact |
| `Board.columns` 1..20, `cards` ≤ 1000 | AC-10 / AC-11 / AC-15 | exact |
| `role` | `Owner`/`Member` projected to `is_owner: boolean` | exact: the only role distinction the spec lets a member see |

**Deliberate relaxation, which would otherwise read as drift:** the request `name` and `title` fields
carry **no `minLength` / `maxLength`**. The spec bounds them *after* trimming Unicode whitespace
(Text rule). A raw `"  x  "` or a 102-character string that trims to 100 is legal, and JSON Schema
cannot state "length after trim". A `maxLength: 100` here would be stricter than the domain and
would make a schema-validating client refuse a valid name. The bound is stated in each field's
`description` and on the response-side `*Text` schemas, where the stored value is already trimmed and
the bound holds exactly. A second reason is ADR 0014: the server does not validate against these
schemas at the door. This is recorded here so that nobody later tightens the fields back.

**4. OpenAPI ↔ sequence** *(supporting)*: **✓** (with three gaps back-fed as OQs, see below)

| §6 flow | operation(s) | branches → responses |
|---|---|---|
| 1 — thin path | `createBoard`, `addCard` | invalid name / 50 owned → 400 / 409; accepted → 201 `Board`; card → 201 |
| 2 — order of checks (edit card) | `editCard` | limit → 429; not a member → 404; not on board → 404; text → 400; stale → 409 `card_changed`; lost at store → 409 `card_changed`; accepted → 200 |
| 3 — two last-column deletes | `deleteColumn` | loser re-decided → 409 `last_column`; winner → 200 |
| 4 — list my boards | `listMyBoards` | empty / some → 200 (`items` empty or not) |
| 5 — open board, then card | `openBoard`, `openCard` | not available → 404; member → 200; card not on board → 404; found → 200 |
| 6 — add / rename column | `addColumn`, `renameColumn` | name → 400; 20 columns → 409; not on board → 404; renamed since → 409; accepted → 201 / 200 |
| 7 — move column | `moveColumn` | not on board → 404; stale → 409 `columns_changed`; accepted → 200; conflict → re-decided (or 503) |
| 8 — delete column | `deleteColumn` | not on board → 404; renamed → 409; holds cards → 409; only column → 409; accepted → 200 |
| 9 — add card | `addCard` | not on board → 404; text → 400; 1,000 cards → 409; accepted → 201 |
| 10 — delete card | `deleteCard` | not on board → 404; stale → 409; accepted → 204 |
| 11 — owner renames / deletes | `renameBoard`, `deleteBoard` | non-owner → 403 `owner_only`; name → 400; mismatch → 409 `confirmation_mismatch`; accepted → 200 / 204 |
| 12 — visitor follows a link | `openBoard` | no session → 401, identical for every id |
| 13 — change after session ended | every change | no session → 401, not counted, nothing changed |

The flow 12 return-address guard and the flow 13 kept-text store are client logic. They have no
contract surface, which is correct and not an orphan-sequence finding.

### Back-feed — coverage cross-check

**Every §5 AC → ≥ 1 operation/response: ✓ (33/33).**

| AC | mapped to |
|---|---|
| AC-01 | `createBoard` 201 (three columns, `is_owner: true`) |
| AC-02 | `createBoard` / `renameBoard` 400 `board_name_invalid` |
| AC-03 | `createBoard` 409 `owned_board_limit_reached` |
| AC-04 | `listMyBoards` 200 |
| AC-05 | `addColumn` 201 |
| AC-06 | `renameColumn` 200, `moveColumn` 200 |
| AC-06b | `renameColumn` / `deleteColumn` 409 `column_renamed` |
| AC-07 | `deleteColumn` 200 |
| AC-08 | `addColumn` / `renameColumn` 400 `column_name_invalid` |
| AC-09 | `deleteColumn` 409 `column_not_empty` |
| AC-10 | `deleteColumn` 409 `last_column` |
| AC-10b | `deleteColumn` 409 `last_column` under a race (flow 3) |
| AC-11 | `addColumn` 409 `column_limit_reached` |
| AC-12 | `addCard` 201 |
| AC-13 | `editCard` 200, `openCard` 200 |
| AC-14 | `addCard` / `editCard` 400 `card_title_invalid` / `card_description_invalid` |
| AC-15 | `addCard` 409 `card_limit_reached` |
| AC-16 | every `*Text` schema: plain text, returned as stored (`info.description`, `CardDescriptionText`). The rendering itself is a client rule (sad.md §6 coverage note) |
| AC-17 | `ChangeRateLimited` 429 on all ten changes |
| AC-18 | `deleteCard` 204, then `boards.not_available` for that card |
| AC-18b | `BoardNotAvailable` 404 on every operation that names a column or card |
| AC-19 | `renameBoard` 200 |
| AC-20 | `deleteBoard` 204, then `boards.not_available` everywhere |
| AC-20b | `deleteBoard` 409 `confirmation_mismatch` with `current_name` |
| AC-21 | no role check on the column and card operations: none declares `owner_only` |
| AC-22 | `renameBoard` / `deleteBoard` 403 `owner_only` |
| AC-23 | `editCard` / `deleteCard` 409 `card_changed` with `current_card` |
| AC-24 | `moveColumn` 409 `columns_changed` with `current_layout` |
| AC-24b | `moveColumn` 200 when only renames or card changes intervened; `editCard` 200 when another card changed |
| AC-25 | `BoardNotAvailable` 404 on all eleven board-scoped operations, answered before shape, text, stale and rule checks |
| AC-26 | `BoardNotAvailable` 404 for another board's `columnId` / `cardId` / `column_id` |
| AC-27 | `openBoard` 401, identical for any id (flow 12) |
| AC-28 | `SessionNotRecognised` 401 on every change: nothing changed, not counted (flow 13) |

**Every operation → a §4 user story + ≥ 1 AC: ✓ (13/13).**

| operation | user story | ACs |
|---|---|---|
| `listMyBoards` | US-02 | AC-04 |
| `createBoard` | US-01 | AC-01, AC-02, AC-03 |
| `openBoard` | US-09, US-10 | AC-20, AC-25, AC-27 |
| `renameBoard` | US-06, US-07 | AC-19, AC-02, AC-22 |
| `deleteBoard` | US-06, US-07 | AC-20, AC-20b, AC-22 |
| `addColumn` | US-03, US-07 | AC-05, AC-08, AC-11, AC-21 |
| `renameColumn` | US-03, US-08 | AC-06, AC-06b, AC-08 |
| `moveColumn` | US-03, US-08 | AC-06, AC-24, AC-24b |
| `deleteColumn` | US-03, US-08 | AC-06b, AC-07, AC-09, AC-10, AC-10b |
| `addCard` | US-04 | AC-12, AC-14, AC-15 |
| `openCard` | US-04 | AC-13, AC-16 |
| `editCard` | US-04, US-08 | AC-13, AC-14, AC-23 |
| `deleteCard` | US-05, US-08 | AC-18, AC-23 |

**Every §6 `alt`-branch → a response: ✓** (table under point 4). **Three responses the contract
needs but no §6 flow shows**, which are the sequence gaps OQ-API-1 to OQ-API-3 below.

---

## Open questions and accepted findings

### OQ-API-1 — retry exhaustion has no branch in any flow *(open, owner: `sequences`)*

- **Finding.** ADR 0015 bounds the retry at 3, and sad.md §7 monitors and alerts on "changes that
  failed after exhausting their 3 retries". No §6 flow shows what the member receives. Flow 3 and
  the persist notes of flows 6–11 end at "reload and re-decide".
- **Contract default (decided with the owner, 2026-09-24).** 503 `boards.contended` with
  `Retry-After: 1`, answered only after the membership check. Nothing is changed, and the attempt
  counts against the change limit. It is declared on every operation that updates the board row or
  the owned-board counter: `createBoard`, `renameBoard`, `deleteBoard`, `addColumn`, `moveColumn`,
  `deleteColumn`, `addCard`, `deleteCard`. It is not declared on `renameColumn` or `editCard`,
  because their only write condition is their own counter, and a loss there is the ordinary stale
  refusal.
- **Owner:** `sequences`, which adds the exhaustion branch to flow 3 (or a note on flows 6–11).
  **Due:** before the contract is finalized.

### OQ-API-2 — an out-of-range column position has no branch *(open, owner: `sequences`)*

- **Finding.** Flow 7 does not show a `position` outside 0..n-1, and no AC covers it.
- **Contract default.** 400 `boards.column_position_invalid`, checked **after** the stale check
  (the value of n is only meaningful for a current view), so the check order is: not on board →
  stale → position range.
- **Owner:** `sequences` (flow 7 gains the branch). If the owner wants it as a spec criterion, that
  is a `specify` edit. **Due:** before the contract is finalized.

### OQ-API-3 — the two body-shape refusals have no branch *(open, owner: `sequences`)*

- **Finding.** sad.md §8 names "a body that is not JSON at all" as refused before membership, and
  ADR 0014 requires every other shape problem to be judged after it. No flow draws either, and no
  AC names them.
- **Contract default.** `boards.request_malformed` (400) is answered before membership, identically
  for every board, and is not counted against the change limit (sad.md §8 Rate limiting). This
  includes a wrong Content-Type, matching the accounts precedent. `boards.request_invalid` (400) is
  answered after membership: the JSON parsed but a required field is absent, a field has the wrong
  type, an unknown field is present, or `editCard` sends neither `title` nor `description`. It is
  counted, because it is a refused attempt under AC-17. It comes right after the membership check and
  before the item-on-board check, because `addCard` needs a readable `column_id` to look the column up.
- **Owner:** `sequences` (a note on flow 2, the canonical order-of-checks flow). **Due:** before the
  contract is finalized.

### F-1 — AC-17's wording vs the two uncounted refusals *(accepted, pre-existing)*

AC-17 literally counts every refusal but the limit's own. The design does not count a failed
antiforgery check or a non-JSON body. The contract follows the design: `ChangeRateLimited` states
both exemptions. This is **already carried** as a sad.md §11 Low row, with "align AC-17's wording" as
its mitigation. No new action.

### F-2 — "identical field for field" and the two request-echo members *(accepted)*

The spec §6 NFR "Indistinguishable refusal" asks for the non-member refusal to be identical, field
for field, to the nonexistent-board refusal. Two members of every problem document echo the request
and not the board. `instance` is the request path, so it differs between any two different ids,
whether they are real or not, and the caller wrote it themselves. `traceId` is unique per request.
Neither reveals whether a board exists. `BoardNotAvailable` declares both, and the QG-1 comparison
test (sad.md §10) must compare every other member and every header, excluding exactly these two. This
is recorded so that `plan-tests` writes the comparison that way and does not stumble on it.

### F-3 — DELETE requests carry a body *(accepted, owner decision 2026-09-24)*

`deleteBoard`, `deleteColumn` and `deleteCard` take a JSON body. OpenAPI 3.1 permits a DELETE body,
and HTTP (RFC 9110) gives it no defined semantics, which is not the same as forbidding it. The board
name must not travel in a URL (sad.md §8 Logging), and one binding path keeps ADR 0014's lenient
binding uniform. **Consequence for `implement`:** minimal API binds a DELETE body only when the
handler asks for it explicitly, and the reverse proxy on the self-hosted host must pass it through.
An integration test on each DELETE proves the body arrives, and the smoke run on the reference
machine proves it through the proxy.

---

## Section C — recorded deviations from the api-skill defaults

Each is mandated by a source in this repository or decided with the owner, and is a **recorded
deviation, not drift**.

| Default | Applied instead | Mandated by |
|---|---|---|
| `{code, message, details?}` envelope | **RFC 9457 `ProblemDetails`** with a `code` extension member; `current_*` and `retry_after_seconds` extension members in place of `details` | CLAUDE.md "Errors are ProblemDetails, from one handler" + sad.md §8; accounts-and-sessions precedent |
| `BearerAuth` global | **`SessionCookie`** (`projector_session`), plus the `X-XSRF-TOKEN` header on every change | ADR 0003, ADR 0008 |
| `next_cursor` as `format: uuid` | an **opaque string** cursor encoding `(created_at, id)`, plus a `prev_cursor` | The list is ordered by `Boards.CreatedAt` from `IClock`, which is not the GUID v7's embedded time (they diverge under `TestClock`), so a bare id cannot be the cursor. Owner decision 2026-09-24 |
| Request string bounds as `minLength`/`maxLength` | bounds stated in `description` on request names/titles and enforced by `BoardText` after trim | spec §5 Text rule (length counted after trimming) + ADR 0014 lenient binding |
| Path ids as `format: uuid` | `type: string`; a non-UUID is a nonexistent item | ADR 0014: nothing may be refused differently before membership |

---

## Lint

`spectral lint contracts/openapi.yaml` with the `spectral:oas` ruleset gives **0 errors, 0
warnings** (run 2026-09-24 with `@stoplight/spectral-cli@6` via `npx`). The first run found one
`invalid-ref` (a missing `AntiforgeryFailed` example), which was fixed before this report was
written.

Spectral is still not wired into a project check target. The accounts report's recommendation
stands: add it next to `dotnet test`, so that a contract edit which breaks OpenAPI 3.1 fails in CI.

## Structural self-check

The bidirectional drift check above **is** this stage's self-check. Result: **3 core points ✓**
(point 2 ✓ with the 20 `boards.*` codes recorded as a proposal for the not-yet-existing
`BoardProblems.cs`, as the reference prescribes), **1 supporting point ✓**, and **33/33 ACs** and
**13/13 operations** mapped. There are **3 open questions** back-fed to `sequences` with owner and
due date, and **3 findings accepted**. The ≥ 3 flags paused the run, and the owner resolved each one
before the contract was written.
