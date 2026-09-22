---
id: T13
title: "Expose POST /api/v1/accounts with the per-source registration rate limit"
layer: "ports"
deps: ["T8", "T11", "T12"]
blocks: ["T18"]
acs: ["AC-01", "AC-01b", "AC-02", "AC-02b", "AC-03", "AC-11b"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "src/Uniqua.Projector.Api/Accounts/RegistrationRateLimit.cs"
  - "src/Uniqua.Projector.Api/Program.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/RegisterEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "M"
context_budget: "M"
status: "done"
---

# T13 — Expose POST /api/v1/accounts with the per-source registration rate limit

## Place in the sequence

- **Blocked by:** T8 — RegisterAccount, T11 — ProblemDetails and antiforgery, T12 — the session cookie · **Blocks:** T18 — quality-scenario tests · **Wave:** 5.
- **Lane:** shares `AccountEndpoints.cs` with T14 and `Program.cs` with T11, T12 and T20 — serialized with T14 in the endpoint lane; `implement` may close the two together under one gate.

## Why (user story)

> **As a** visitor
> **I want** to create an account from the public link with no help from anyone
> **So that** I can reach the product on my own
>
> — `spec.md §4, US-01, verbatim` · full text: [spec.md](../spec.md)

This task is the public front door itself: the one endpoint a stranger reaches with no help, and the limit that keeps it from being a machine for making accounts.

## Inlined context

> `Api->>Api: Identify the request source from the client address the reverse proxy reports, trusted only because the request arrived from the proxy` → `alt Five registrations already came from this source within the past minute` → `Api-->>Spa: Refused - registration is temporarily limited, and when it may be tried again` → `else Within the limit` → `Api->>App: Register this account`
>
> — `sad.md §6, flow 3, first branch, abridged` · full text: [sad.md](../sad.md)

> *Spam account creation*: rate limit — no more than 5 registrations per minute per request source, where a **request source** is the client address as reported by this instance's own reverse proxy. That report is trusted only when the request arrives from the proxy itself: trusting it unconditionally would let anyone forge the key and make the limit decorative, and ignoring it entirely would key every visitor to the proxy's own address and close registration for the whole world after five accounts. Accepted residual risk — people behind one shared outbound address share a limit.
>
> — `spec.md §6.1, Abuse cases, verbatim` · full text: [spec.md](../spec.md)

> One instance on the owner's self-hosted host, behind a **reverse proxy** that terminates TLS for the registered domain and forwards the originating client address — that forwarded address is the **request source** the registration rate limit keys on (spec §6.1), and it is trusted only when the request arrives from the proxy itself.
>
> — `sad.md §7, Deployment view, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — rate limiting:** No more than 5 registrations per minute per request source — the client address as reported by the reverse proxy, trusted only when the request arrives from the proxy.
>
> — `sad.md §8, Rate limiting, verbatim` · full text: [sad.md](../sad.md)

> **Hard rule — monitoring:** Registrations refused by the rate limit, and the request source that triggered it.
>
> — `sad.md §7, Monitoring, verbatim` · full text: [sad.md](../sad.md)

> | Latency p95, registration | ≤ 800 ms | server-side timing, sampled in the smoke run |
>
> — `spec.md §6, NFR row, verbatim` · full text: [spec.md](../spec.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [openapi.yaml](../contracts/openapi.yaml)) and follow it. Do not guess.

## Data delta

No DB changes. The rate-limit counter is in-process state keyed by request source, not a table — nothing in `data-model.md` carries it, and flow 3's postcondition says a refused registration writes "no account, no session, no counter".

— `data-model.md §Entities, abridged (no entity for the limit)` · full text: [data-model.md](../data-model.md)

## API contract

- `POST /api/v1/accounts` → `201` `Account` + `Set-Cookie` · errors: `400 accounts.password_invalid`, `400 accounts.email_invalid`, `403 accounts.antiforgery_failed`, `409 accounts.email_taken`, `409 accounts.display_name_taken`, `429 accounts.registration_rate_limited`.
- `security: []` — registration is reached with no session.
- Request fields: `email` (`format: email`, ≤ 256), `password` (8..128, `writeOnly`), `display_name` (1..50).
- Response: `id`, `email`, `display_name`, and the session cookie set on the `201`. `additionalProperties: false`.
- The `429` carries a `Retry-After` header in seconds **and** a `retry_after_seconds` extension member, and "states when a further attempt may be made".

— `contracts/openapi.yaml, operationId registerAccount, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-01 — happy path

> **Given** a visitor with no account, on the public link
> **When** they submit an unused email address, a password of at least 8 and at most 128 characters, and an unused display name of at most 50 characters
> **Then** the system creates the account, opens a session immediately without asking them to sign in again, and shows them their own display name
>
> — `spec.md §5, AC-01, verbatim` · full text: [spec.md](../spec.md)

### AC-01b — error

> **Given** 5 accounts have already been registered from the same request source within the past minute
> **When** a further visitor from that same source submits the registration form
> **Then** the system refuses, says plainly that registration is temporarily limited, and tells them when they may try again
>
> — `spec.md §5, AC-01b, verbatim` · full text: [spec.md](../spec.md)

### AC-02 — error

> **Given** a visitor filling in the registration form
> **When** they submit a password shorter than 8 characters
> **Then** the system refuses to create the account and tells them, in plain language, that a password must be at least 8 characters long, leaving everything else they typed in place
>
> — `spec.md §5, AC-02, verbatim` · full text: [spec.md](../spec.md)

### AC-02b — error

> **Given** a visitor filling in the registration form
> **When** they submit something that cannot be an email address
> **Then** the system refuses to create the account, says plainly that the address is not usable, and leaves everything else they typed in place
>
> — `spec.md §5, AC-02b, verbatim` · full text: [spec.md](../spec.md)

### AC-03 — domain invariant

> **Given** an account already exists for a given email address
> **When** a visitor tries to register with that same address
> **Then** the system refuses and states plainly that the address is already registered, because an email address identifies exactly one account
>
> — `spec.md §5, AC-03, verbatim` · full text: [spec.md](../spec.md)

### AC-11b — domain invariant

> **Given** an account already uses a given display name
> **When** a visitor tries to register with that same display name
> **Then** the system refuses and states plainly that the name is taken, because a display name identifies exactly one account to the people who see it
>
> — `spec.md §5, AC-11b, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `MapAccountEndpoints()` with the registration route, called once from `Program.cs` — `src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs`.
- [ ] `POST /api/v1/accounts` calling `RegisterAccount` (T8), issuing the session cookie via `SessionCookie` (T12) on success, and returning `201` with `id`, `email`, `display_name`.
- [ ] Map each sentinel error to its status through T11's handler; build no error body here.
- [ ] Forwarded-header handling configured so the client address is taken from the proxy's report **only** when the request came from the proxy, and from the connection otherwise — `src/Uniqua.Projector.Api/Program.cs`.
- [ ] `RegistrationRateLimit` — 5 per minute per request source, refusing with `429`, `Retry-After` and `retry_after_seconds` — `src/Uniqua.Projector.Api/Accounts/RegistrationRateLimit.cs`.
- [ ] The limit is evaluated **before** the use case, so a refused registration touches no store (flow 3 postcondition).
- [ ] Log every rate-limit refusal with the request source (sad §7 monitoring) — and never log the submitted address.
- [ ] Integration tests: the happy path sets a usable cookie; the 6th registration in a minute is refused with a usable `Retry-After`; each validation and uniqueness refusal returns its contract code.

## Edge cases

| Case | Behaviour |
|---|---|
| The request does not come from the proxy but carries a forwarded-for header | The header is ignored; the connection address is the key. Otherwise the limit is decorative |
| The request comes from the proxy with no forwarded address | Fall back to the connection address — that keys every visitor to the proxy and is the failure mode spec §6.1 names, so it is logged loudly rather than passed over |
| Several visitors behind one shared outbound address | They share a limit. Accepted residual risk, stated in spec §6.1 |
| The 6th registration arrives 61 seconds after the first | Accepted — the window has moved |
| A refused `429` is retried immediately | Refused again with a smaller `Retry-After`; the counter is not extended by the refusal itself |
| A malformed JSON body | `400` through the one handler, with no framework message echoed |
| A successful registration with `additionalProperties` in the body | Refused by the contract's `additionalProperties: false` |
| The instance restarts | The in-process counter is lost and up to 5 more registrations are possible from one source. Accepted: the limit is anti-spam hygiene, not an authorization control |

## Definition of Done

- [ ] An integration test drives AC-01 end to end: `201`, a cookie that is immediately accepted by `GET /api/v1/accounts/me`, and the display name in the body.
- [ ] An integration test proves the 6th registration from one source within a minute is refused `429 accounts.registration_rate_limited` with a `Retry-After` that is a positive number of seconds (AC-01b).
- [ ] An integration test per refusal (AC-02, AC-02b, AC-03, AC-11b) asserts the contract's status and `code`, and that nothing was written.
- [ ] A test proves a forwarded client address is honoured only when the request arrives from the configured proxy.
- [ ] A test proves the endpoint is reachable with no session (`security: []`) and requires the antiforgery token.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
