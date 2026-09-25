---
id: T12
title: "Limit each account to 120 board change attempts per rolling minute, before the membership check"
layer: "ports"
deps: ["T9"]
blocks: ["T13", "T15"]
acs: ["AC-17", "AC-28"]
files_hint:
  - "src/Uniqua.Projector.Api/Boards/BoardChangeRateLimit.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardEndpoints.cs"
  - "src/Uniqua.Projector.Api/Boards/BoardProblems.cs"
  - "src/Uniqua.Projector.Api/DependencyInjection.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Boards/BoardChangeRateLimitTests.cs"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "todo"
---

# T12 — Limit each account to 120 board change attempts per rolling minute, before the membership check

## Place in the sequence

- **Blocked by:** T9 — Expose the board endpoints with lenient binding and map every board refusal to one problem document · **Blocks:** T13 — Prove the membership boundary across every read and change kind, and map the four board rules to their tests, T15 — Measure opening a full board, a single change and change throughput against the §6 budgets · **Wave:** 6 — it filters the route group T9 creates; it runs beside T10/T11 in the graph but shares their lane.
- **Lane:** shares `BoardEndpoints.cs` and `BoardProblems.cs` with T9, T10, T11 — serialized with them.

## Why (user story)

> **As a** board member
> **I want** to add a card with a title and an optional description to a column, and edit both later
> **So that** each piece of work is written down where everyone on the board can see it
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

This task bounds how fast any one account can change boards, without the refusal telling it anything about any board.

## Inlined context

> **The per-account change limit is an Api endpoint filter.** It must run after the session is recognised and before the membership check (spec §6.1), and it depends only on the account, so it sits on the `/api/v1/boards` route group as `BoardChangeRateLimit`, reusing the existing in-memory `SlidingWindowLimiter` keyed by account id: 120 attempts per rolling minute, a slot reserved per attempt and kept whatever the outcome, an attempt it refuses not counted (AC-17). Reads are not counted.
>
> — `sad.md §5, Building block view, verbatim` · full text: [sad.md](../sad.md)

> **Two refusals are not counted either**, although AC-17 literally counts every refusal other than the limit's own: a refused antiforgery token (answered before the session, so there is no account to count against […]) and a body that is not JSON (answered by the framework before the route handler, identical for every board, so it reveals nothing). In memory, per instance (§7).
>
> — `sad.md §8, Rate limiting, abridged` · full text: [sad.md](../sad.md)

> The rolling minute of the change limit is measured on [`IClock`], replaced by `TestClock` in integration tests.
>
> — `sad.md §8, Time, abridged` · full text: [sad.md](../sad.md)

> **Hard rule:** A new dependency is registered in its own layer's `DependencyInjection.cs`, not in `Program.cs`.
>
> — `CLAUDE.md §Layer wiring, verbatim` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read [sad.md](../sad.md) §5/§8 and `src/Uniqua.Projector.Api/Accounts/SlidingWindowLimiter.cs` in full and follow them. Do not guess.

## Data delta

No DB changes.

## API contract

- Every change operation (`createBoard`, `renameBoard`, `deleteBoard`, `addColumn`, `renameColumn`, `moveColumn`, `deleteColumn`, `addCard`, `editCard`, `deleteCard`) → `429` `boards.change_rate_limited`, header `Retry-After` (seconds until the oldest counted attempt leaves the minute), body member `retry_after_seconds`; title «Changes are temporarily limited», detail «You have made many changes in the past minute. You can continue shortly.»
- Not counted: `listMyBoards`, `openBoard`, `openCard`, 401 no session, 403 antiforgery, 400 `request_malformed`.

— `contracts/openapi.yaml, components.responses.ChangeRateLimited, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-17 — error

> **Given** an account that has attempted 120 changes within the past minute — every attempt to create, rename, reorder, edit or delete a board, column or card counts, whichever board it named, whether or not they belong to it, and whether it was accepted or refused for any reason other than this limit
> **When** they attempt a further change
> **Then** the system refuses it, says plainly that changes are temporarily limited, tells them when they may continue, and changes nothing on any board — answering identically whichever board the change named, so the refusal reveals nothing about any board. An attempt refused by this limit does not itself count, so an account that stops is admitted again once its earlier attempts fall out of the minute; an attempt made with no active session (AC-28) belongs to no account and does not count
>
> — `spec.md §5, AC-17, verbatim` · full text: [spec.md](../spec.md)

### AC-28 — cross-context

> **Given** a board member whose session has ended — they signed out on this browser, or it expired — while the board is still open in front of them
> **When** they submit a change
> **Then** the system changes nothing on the board, presents the sign-in form, and does not treat the change as coming from that member; what they typed is kept in this browser so that, once they sign in again as the same account, they can apply it again, and it is discarded, never shown, if a different account signs in there
>
> — `spec.md §5, AC-28, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `BoardChangeRateLimit` endpoint filter over `SlidingWindowLimiter`, keyed by the recognised account id, 120 / 60 s, reserve-then-keep; applied to change endpoints only — `Api/Boards/BoardChangeRateLimit.cs`.
- [ ] Attach to the group's change endpoints — `Api/Boards/BoardEndpoints.cs`; register the singleton in `AddBoardsApi` — `Api/DependencyInjection.cs`.
- [ ] Wording + `Retry-After` / `retry_after_seconds` — `Api/Boards/BoardProblems.cs`.
- [ ] Metric `boards.change_rate_limited` by account (sad §7).
- [ ] Integration tests with `TestClock` — `tests/.../Boards/BoardChangeRateLimitTests.cs`, modelled on `Accounts/SignInRateLimitTests.cs`.

## Edge cases

| Case | Behaviour |
|---|---|
| 120 attempts all refused `not_available` on boards the caller is not a member of | All counted; the 121st is 429 |
| The 121st is aimed at a board the caller owns vs. one that never existed | Identical 429 in both |
| Account keeps retrying while limited | Refused attempts are not counted; admitted once the oldest leaves the minute |
| Change with an ended session (AC-28) | 401, not counted, nothing changed |
| 500 reads in a minute | Never limited |

## Definition of Done

- [ ] Integration tests pass for every AC above and every edge-case row against `TestClock`.
- [ ] A test proves nothing changed on any board for a 429.
- [ ] every Hard Rule inlined above still holds
- [ ] `dotnet build`, `dotnet format --verify-no-changes` clean
