---
id: T9
title: "Expose the board endpoints with lenient binding and map every board refusal to one problem document"
layer: "ports"
deps: ["T6"]
blocks: ["T10", "T11", "T12"]
acs: ["AC-01", "AC-02", "AC-03", "AC-04", "AC-19", "AC-20", "AC-20b", "AC-22", "AC-25", "AC-27"]
files_hint:
  - "src/Uniqua.Projector.Api/Boards/BoardEndpoints.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardRequestBody.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardProblems.cs"
  - "src/Uniqua.Projector.Api/ProblemDetailsSetup.cs"
  - "src/Uniqua.Projector.Api/DependencyInjection.cs"
  - "src/Uniqua.Projector.Api/Program.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Fixtures/BoardFixtures.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "L"
context_budget: "M"
status: "todo"
---

# T9 — Expose the board endpoints with lenient binding and map every board refusal to one problem document

## Place in the sequence

- **Blocked by:** T6 — Write the board use cases: create, list, open, rename and delete · **Blocks:** T10 — Expose the column endpoints: add, rename, move and delete, T11 — Expose the card endpoints: add, open, edit and delete, T12 — Limit each account to 120 board change attempts per rolling minute, before the membership check · **Wave:** 5 — it establishes the `/api/v1/boards` route group, the lenient body reader, `BoardProblems` and `BoardFixtures`, which T10–T12 extend.
- **Lane:** shares `BoardEndpoints.cs`, `BoardProblems.cs` and `BoardFixtures.cs` with T10, T11, T12 — serialized with them.

## Why (user story)

> **As a** board owner
> **I want** any account that is not a member to be unable to see, change, or even confirm the existence of my board
> **So that** what I write on it is shared only with the people I chose
>
> — `spec.md §4, US-09, verbatim` · full text: [spec.md](../spec.md)

> **As a** visitor
> **I want** a link to a board to send me to sign in without revealing anything about that board
> **So that** a link that reaches the wrong person gives nothing away
>
> — `spec.md §4, US-10, verbatim` · full text: [spec.md](../spec.md)

This task puts the board actions on the wire, and makes every refusal a board can produce come from the one handler, worded in one file.

## Inlined context

> **Order of checks on every request**: antiforgery token (on changes) → session → a body that is not JSON at all → per-account change limit (changes only) → member-scoped board load → request shape → item on this board → owner check (board rename and delete only) → text limits → stale check → ceilings and column rules. Everything after the board load is answered only to a member. Everything before it is identical for every board.
>
> **Binding is lenient** (ADR 0014). The server accepts any JSON body as it arrives and judges its shape only after the membership check. […] A body that is not JSON at all is the one exception: it is refused earlier, identically for every board. Path identifiers are accepted as any string, and a value that is not a UUID is simply a board, column or card that does not exist.
>
> — `contracts/openapi.yaml, info.description, verbatim` · full text: [openapi.yaml](../contracts/openapi.yaml)

> `BoardNotAvailable` — status, headers and body are identical in every case […]. Two members echo the request itself and so differ between any two requests, whichever board they name: `instance` […] and `traceId`. The spec §6 "indistinguishable refusal" comparison excludes exactly those two.
>
> — `contracts/openapi.yaml, components.responses.BoardNotAvailable, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

> **Hard rule:** Every failure response is an RFC 9457 `application/problem+json` document produced by the handler in `src/Uniqua.Projector.Api/ProblemDetailsSetup.cs`. An endpoint never builds an error shape of its own and never returns a bare status code with an ad-hoc body.
>
> — `CLAUDE.md §Errors are ProblemDetails, verbatim` · full text: [CLAUDE.md](../../../../CLAUDE.md)

> **Hard rule:** Each layer exposes one `AddXxx(IServiceCollection)` extension, called from `Program.cs`. That file names no type from inside a layer.
>
> — `CLAUDE.md §Layer wiring, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

> `BoardFixtures.ABoardAsync(owner, name)` creates a board through the API; `BoardFixtures.AMemberOfAsync(boardId, account)` inserts a `BoardMemberships` row with `Role = Member` **directly through `AppDbContext`** — the only way a non-owner member exists in this feature; `AnAccountOwningBoardsAsync(count)` inserts directly and sets `OwnedBoardCount` to match.
>
> — `data-model.md §Test fixtures, abridged` · full text: [data-model.md](../data-model.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([openapi.yaml](../contracts/openapi.yaml) · [sad.md](../sad.md) §8 · [adr/0014](../adr/0014-enforce-membership-by-loading-the-board-scoped-to-the-caller.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

| Operation | Success | Refusals this task owns |
|---|---|---|
| `GET /api/v1/boards` `listMyBoards` | 200 `BoardListPage` (`items[{id,name,created_at,is_owner}]`, `has_next`, `has_prev`, `next_cursor`, `prev_cursor`) | 400 `boards.request_invalid` (cursor/limit) |
| `POST /api/v1/boards` `createBoard` `{name}` | 201 `Board` | 400 `request_malformed` / `request_invalid` / `board_name_invalid`; 409 `owned_board_limit_reached`; 503 `contended` |
| `GET /api/v1/boards/{boardId}` `openBoard` | 200 `Board` | 404 `not_available` |
| `PATCH /api/v1/boards/{boardId}` `renameBoard` `{name}` | 200 `BoardName {id,name}` | 400 as above; 403 `owner_only`; 404; 503 |
| `DELETE /api/v1/boards/{boardId}` `deleteBoard` `{confirm_name}` | 204 | 400; 403 `owner_only`; 404; 409 `confirmation_mismatch` + `current_name`; 503 |

Every code is `boards.<name>`; every body is `type`, `title`, `status`, `detail`, `code` (+ `instance`, `traceId`). Wording per the contract's examples, e.g. `boards.not_available` → title «Board not available», detail «This board does not exist, or you are not a member of it.», status 404. 401 `accounts.session_not_recognised` and 403 `accounts.antiforgery_failed` are inherited and unchanged.

— `contracts/openapi.yaml, paths /api/v1/boards + /{boardId}, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

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

### AC-27 — cross-context

> **Given** a visitor with no active session
> **When** they open a link to a board
> **Then** the system presents the sign-in form and reveals nothing about the board — not its name and not whether it exists — answering a link to a real board exactly as it answers one to a board that never existed; once they sign in, they are returned to that link's address and answered there as any signed-in account is — the board if they are a member, otherwise the refusal of AC-25
>
> — `spec.md §5, AC-27, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `BoardEndpoints.MapBoardEndpoints` — `/api/v1/boards` group, `RequireAuthorization()`, the five operations — `Api/Boards/BoardEndpoints.cs`.
- [ ] `BoardRequestBody` — read the body as `JsonElement` (lenient), refuse only non-JSON before membership (`boards.request_malformed`), expose typed getters the handler uses **after** the use case's load — `Api/Boards/BoardRequestBody.cs`.
- [ ] `BoardProblems` — the wording of every board code, including those T10/T11 raise, so the table exists once — `Api/Boards/BoardProblems.cs`.
- [ ] Map `BoardError` → problem in `ProblemDetailsSetup`; extend the malformed-body reshaping to `/api/v1/boards` paths — `Api/ProblemDetailsSetup.cs`.
- [ ] `AddBoardsApi()` in `Api/DependencyInjection.cs`; `app.MapBoardEndpoints()` in `Program.cs` (the extension, never an inner type).
- [ ] `BoardFixtures` with `ABoardAsync`, `AMemberOfAsync`, `AnAccountOwningBoardsAsync` — `tests/.../Fixtures/BoardFixtures.cs`.
- [ ] Endpoint tests — `tests/.../Boards/BoardEndpointTests.cs`.

## Edge cases

| Case | Behaviour |
|---|---|
| `boardId` = `not-a-guid` | 404 `boards.not_available`, identical to an unknown GUID |
| Non-member sends `{"name": ""}` to rename | 404 `not_available` — not 400, not 403 |
| Body `{` (not JSON) to any board | 400 `boards.request_malformed`, identical for every board |
| Member sends `{"name": 5}` | 400 `boards.request_invalid`, only after membership |
| Delete with the name in the query string | Ignored — the confirmation travels only in the body |
| No session on `GET /api/v1/boards/{id}` | 401 `accounts.session_not_recognised`, identical for any id |

## Definition of Done

- [ ] Endpoint integration tests pass for every AC above and every edge-case row, the non-owner cases against `AMemberOfAsync`.
- [ ] A test compares the non-member and never-existed `not_available` responses status, headers and body, excluding `instance` and `traceId`.
- [ ] `Program.cs` names no type from inside a layer.
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
