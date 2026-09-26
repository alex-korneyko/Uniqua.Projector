---
id: T23
title: "Harden the board request binding: lone surrogates, unknown fields, unparsable item ids and empty delete bodies"
layer: "ports"
deps: []
acs: ["AC-17", "AC-18b"]
files_hint:
  - "src/Uniqua.Projector.Api/Boards/BoardRequestBody.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardEndpoints.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardEndpoints.Columns.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardEndpoints.Cards.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/ColumnEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/CardEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "S"
status: "todo"
origin: "review 2026-09-26 — findings Q1, B6a, B6c, Q4d"
---

# T23 — Harden the board request binding: lone surrogates, unknown fields, unparsable item ids and empty delete bodies

## Place in the sequence

- **Origin:** follow-up from the independent review, findings Q1, B6a, B6c, Q4d — [review-2026-09-26.md](../_review/review-2026-09-26.md). The finding text there is the source; this brief restates it.
- **Blocked by:** —

## What is wrong and what to do

- **Q1** — `BoardRequestBody.TryGetString` (`BoardRequestBody.cs:27`) calls `GetString()`, which throws `InvalidOperationException` on a lone-surrogate escape such as `{"name":"\ud800"}`; every change endpoint answers 500. Treat that value as not-a-string so it goes down the normal post-membership `boards.request_invalid` path (same for `TryGetOptionalString`, `BoardEndpoints.Cards.cs:235`).
- **B6a** — every request schema in `contracts/openapi.yaml` (1459-1576) has `additionalProperties: false`, and `contracts/api-sync-report.md` OQ-API-3 answers an unknown member with `boards.request_invalid` after membership. Today `{"name":"x","extra":1}` is accepted. Reject members outside each operation's schema in the same post-membership shape branch.
- **B6c** — a non-UUID column or card path id short-circuits to `not_available` before the shape check (`BoardEndpoints.Columns.cs:86,125,164`, `BoardEndpoints.Cards.cs:89,121,164`). The contract (`components.parameters.ColumnId`/`CardId`) treats it as an id that does not exist, reached after the shape check. Map an unparsable column/card id to `Guid.Empty`, as `AddCardAsync` already does for `column_id` (`Cards.cs:62`). Keep the early short-circuit for `boardId` only.
- **Q4d** — `deleteBoard` binds a non-nullable `JsonElement` (`BoardEndpoints.cs:193`), so an empty body is refused by the framework as `request_malformed` before membership, while `deleteColumn`/`deleteCard` bind `JsonElement?` and answer `request_invalid` after it. Bind `JsonElement?` and pass `body ?? default`, as the other two do.

## Acceptance criteria

Re-read AC-17, AC-18b verbatim in [spec.md](../spec.md) §5 before writing the test; the test asserts the business-observable outcome the AC names.

**Fallback:** if this brief is insufficient or contradicted by the code, read the cited file:line, [spec.md](../spec.md), [sad.md](../sad.md), [contracts/openapi.yaml](../contracts/openapi.yaml), [screens.md](../screens.md) and [test-plan.md](../test-plan.md). Do not guess.

## Definition of Done

- [ ] Integration tests (RED first) prove: a lone-surrogate string in each text-carrying change answers 400 `boards.request_invalid` (not 500) for a member and the non-member refusal for a non-member; an unknown member answers 400 `boards.request_invalid` for a member on each body-carrying operation; a non-UUID column/card id with a bad body answers the same as a well-formed missing id with a bad body; an empty-body deleteBoard answers `boards.request_invalid` after membership. `dotnet build` and `dotnet format --verify-no-changes` clean.
- [ ] Test written first and seen failing for the right reason (quote the failing line).
- [ ] Every CLAUDE.md rule still holds (layer direction, domain rules in Domain, ProblemDetails from the one handler, Tailwind only, permissive licences).
