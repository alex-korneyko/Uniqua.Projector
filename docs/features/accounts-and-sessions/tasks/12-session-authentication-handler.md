---
id: T12
title: "Recognise a session on every request: the authentication handler, the cookie and the key ring"
layer: "ports"
deps: ["T2", "T5", "T6", "T11"]
blocks: ["T13", "T14", "T18", "T19"]
acs: ["AC-06", "AC-07", "AC-07b", "AC-10"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/SessionAuthenticationHandler.cs"
  - "src/Uniqua.Projector.Api/Accounts/SessionCookie.cs"
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.Me.cs"
  - "src/Uniqua.Projector.Api/Program.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionRecognitionTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "todo"
---

# T12 — Recognise a session on every request: the authentication handler, the cookie and the key ring

## Place in the sequence

- **Blocked by:** T2 — the expiry rules, T5 — the key-ring table, T6 — the session reader port, T11 — the ProblemDetails handler · **Blocks:** T13 — registration endpoint, T14 — sessions endpoints, T18 — quality tests, T19 — the written session rules · **Wave:** 4.
- **Lane:** shares `src/Uniqua.Projector.Api/Program.cs` with T11, T13 and T20 — serialized.

## Why (user story)

> **As an** account
> **I want** my session to survive closing the browser
> **So that** returning to the link days later does not cost me a sign-in
>
> **— and —**
>
> **As an** account
> **I want** signing out to end this browser's session across every channel at once
> **So that** leaving a shared machine does not leave the product open behind me
>
> — `spec.md §4, US-03 + US-04, verbatim` · full text: [spec.md](../spec.md)

This task is the code that runs on every authenticated request in the product's life. It recognises a live session, refuses a dead one, and issues the cookie that carries it.

## Inlined context

> The one placement that is not simply inherited is **session recognition**. It runs on every authenticated request, so it belongs in the request pipeline in `Api` — but it must read a session record, which only `Infrastructure` may do. It is therefore an Api-level authentication handler that calls an **Application port** (`ISessionReader`), implemented in Infrastructure. The rule that a session is expired — 14 days idle or 90 days old — lives on the **Session entity in Domain**, not in the handler, so that the handler asks the entity rather than re-deriving the arithmetic.
>
> — `sad.md §5, Building block view, verbatim` · full text: [sad.md](../sad.md)

> `Api->>Api: The authentication handler reads the opaque session reference out of the cookie` → `Api->>Infra: Read the session record for this reference, through the session-reader port` → `alt No such record, or the record says the session was revoked` → `Api-->>Spa: Not recognised, whatever the browser still holds` → `else A record was found` → `Api->>Domain: Is this session expired as of now` → `alt Expired` → `Api-->>Spa: Not recognised` → `else Still live` → `Api->>Infra: Stamp this request as activity on the session` (*written at most once an hour*) → `Api-->>Spa: Recognised, the request proceeds`
>
> — `sad.md §6, flow 5 «recognising a session on an ordinary read», abridged` · full text: [sad.md](../sad.md)

> *Postcondition:* recognition cost one indexed read of the session record - the 30 ms spec section 6 budgets for an ordinary read
>
> — `sad.md §6, flow 5 postcondition, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — authentication:** Session cookie — httpOnly, secure, same-site — carrying an opaque reference, recognised on every request by an Api authentication handler against a session record.
>
> — `sad.md §8, Authentication, verbatim` · full text: [sad.md](../sad.md)

> *Session theft*: the session cookie is unreadable by page scripts; a stolen session dies after 14 days without use and, however actively it is used, 90 days after it was opened (AC-07, AC-07b).
>
> — `spec.md §6.1, Abuse cases, abridged` · full text: [spec.md](../spec.md)

> **Hard rule — risk:** One indexed session lookup on every authenticated request, against the ≤ 30 ms budget (ADR 0008's cost). The lookup is by primary key; alert on sustained p95 above 30 ms; re-measure above roughly 100k live rows.
>
> — `sad.md §11, risk row, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([sad.md](../sad.md) · [openapi.yaml](../contracts/openapi.yaml) · [adr/0008](../adr/0008-store-sessions-as-server-side-records.md) · [adr/0009](../adr/0009-keep-the-data-protection-key-ring-in-the-database.md)) and follow it. Do not guess.

## Data delta

No schema change. This task reads one row by primary key and writes one column at most once an hour:

| Column | Type | Constraints | Change |
|---|---|---|---|
| `Sessions.Id` | `uniqueidentifier` | PK | read — the opaque value out of the cookie, and the only lookup key |
| `Sessions.LastSeenAt` | `datetimeoffset` | NOT NULL | written at most once an hour, through `ISessionStore.StampActivity` |
| `DataProtectionKeys.*` | — | — | read at start-up and on key-ring refresh, by the framework |

— `data-model.md §Entities, tables Sessions + DataProtectionKeys, abridged` · full text: [data-model.md](../data-model.md)

## API contract

- `GET /api/v1/accounts/me` → `200` `Account` · errors: `401 accounts.session_not_recognised`.
- It is the canonical request "reserved for a signed-in account" of flow 5 — the client calls it on load to decide whether to show the account or the sign-in form.
- One `401` code covers all four refusals — absent cookie, revoked session, 14-day idle, 90-day absolute — "because the client's response to each is identical: present the sign-in form."
- The cookie itself: `projector_session=<opaque>; Path=/; HttpOnly; Secure; SameSite=Lax`. Its value is the `Sessions.Id` reference; it never appears in a body, and the contract deliberately offers no way for a page script to read it.

— `contracts/openapi.yaml, operationId getCurrentAccount + components.securitySchemes.SessionCookie + responses.SessionNotRecognised, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-06 — happy path

> **Given** an account with an active session
> **When** they close the browser entirely and return to the link the next day
> **Then** the system still recognises them and shows them their own display name, without asking them to sign in
>
> — `spec.md §5, AC-06, verbatim` · full text: [spec.md](../spec.md)

### AC-07 — domain invariant

> **Given** an account whose session has carried no request for 14 days — any request made on the account's behalf counts as activity, including one that only reads
> **When** they return to the link
> **Then** the system no longer recognises them and presents the sign-in form, because a session ends after 14 days of inactivity
>
> — `spec.md §5, AC-07, verbatim` · full text: [spec.md](../spec.md)

### AC-07b — domain invariant

> **Given** an account whose session was opened 90 days ago and has been used steadily ever since
> **When** they return to the link
> **Then** the system no longer recognises them and presents the sign-in form, because no session outlives 90 days however actively it is used
>
> — `spec.md §5, AC-07b, verbatim` · full text: [spec.md](../spec.md)

### AC-10 — authorization

> **Given** a visitor whose session has ended, by signing out or by expiry
> **When** they attempt to reach anything reserved for a signed-in account
> **Then** the system refuses and presents the sign-in form, regardless of what their browser still holds
>
> — `spec.md §5, AC-10, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `SessionCookie` — the name, and the httpOnly / secure / same-site / path attributes in one place, plus issue and clear helpers used by T13 and T14 — `src/Uniqua.Projector.Api/Accounts/SessionCookie.cs`.
- [ ] `SessionAuthenticationHandler : AuthenticationHandler<...>` — read the reference, `ISessionReader.Find`, ask `Session.IsExpired(clock.Now)` and `IsRevoked`, then succeed or fail — `src/Uniqua.Projector.Api/Accounts/SessionAuthenticationHandler.cs`.
- [ ] On success, build the principal from the session's account id and call `ISessionStore.StampActivity`, which itself decides whether an hour has passed.
- [ ] The handler contains **no** date arithmetic: 14 and 90 appear nowhere in this file.
- [ ] Point Data Protection at the key-ring table from T5 and set the application name explicitly, so a redeploy keeps every unexpired session.
- [ ] `GET /api/v1/accounts/me` returning `id`, `email`, `display_name` for the recognised account — `src/Uniqua.Projector.Api/Accounts/AccountEndpoints.Me.cs`.
- [ ] Refusals go through T11's handler as `401 accounts.session_not_recognised` — one code for all four cases.
- [ ] Integration tests for each branch of flow 5, driven by the controllable clock and the `data-model.md` session fixtures.

## Edge cases

| Case | Behaviour |
|---|---|
| No cookie at all | `401 accounts.session_not_recognised` — the same body a revoked or expired session gets |
| A cookie whose value is not a valid session reference | Same `401`; no lookup is attempted on a malformed value |
| A cookie signed by a key that is no longer in the ring | Same `401` — indistinguishable from an unknown session, as it should be |
| A session revoked one second ago | Refused by the record, not by the cookie's absence (AC-10) |
| A session idle 14 days and 30 minutes | Refused. Within the 14 days + at most 1 hour the §6 accuracy row allows, since the last stamp may be up to an hour stale |
| A session opened 90 days ago, used continuously | Refused (AC-07b); activity is irrelevant to the absolute ceiling |
| Two concurrent requests on one live session | Both recognised; at most one writes the activity stamp |
| The instance is redeployed mid-session | Still recognised — the key ring came from the database (T5, ADR 0009) |
| A page script tries to read the cookie | It cannot: httpOnly. The contract exposes no endpoint that returns the reference either |

## Definition of Done

- [ ] An integration test per flow-5 branch passes: live, absent, revoked, 14-day idle, 90-day absolute — each asserting `GET /api/v1/accounts/me` returns `200` with the display name or `401 accounts.session_not_recognised`.
- [ ] A test proves recognition issues exactly one primary-key query and writes nothing when the last activity stamp is under an hour old.
- [ ] A test proves the cookie is set httpOnly, secure and same-site, and that its value is an opaque reference appearing in no response body.
- [ ] A test proves the four refusal causes are byte-identical in their response bodies.
- [ ] `grep` finds no `14` or `90` day literal in `src/Uniqua.Projector.Api/` — the rules live on the `Session` entity only.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
