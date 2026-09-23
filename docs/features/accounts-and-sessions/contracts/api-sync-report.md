---
status: Draft
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-21"
feature_size: "M"
---

# API sync report — accounts-and-sessions

Companion to `contracts/openapi.yaml`. The contract is a **derived** artifact: `data-model.md`
(typed shape) + `sad.md` §6 sequences (error branches) + `spec.md` §4/§5 (endpoint list, observable
outcomes) → OpenAPI. This report is the evidence that the derivation held.

**Inputs read.** `data-model.md` ✓ (present — the default path, no fast-lane skip) · `sad.md` §6 ✓
(seven flows) · `sad.md` §8 ✓ (crosscutting conventions) · `spec.md` §4/§5/§6/§6.1 ✓ ·
`docs/architecture-map.md` §Conventions ✓ · `adr/0003`, `adr/0007`, `adr/0008`, `adr/0010` ✓ ·
`CONTEXT.md` §Glossary ✓ · `.size` = `M` · `.route` = `standard`.

**Interface kind.** `sad.md` frontmatter `target_surfaces: [backend-service, web-frontend]` — **read,
not re-derived**. `backend-service` sub-kind is HTTP/REST → `contracts/openapi.yaml`.
`web-frontend` *consumes* this contract and authors none of its own.

**`contracts/events.md` — N/A, deliberately.** The feature has no asynchronous message contract. The
two candidates were weighed and both fall short of one: the `ISessionRevocationNotifier` call in §6
flow 2 is an in-process port to the realtime hub — no broker, no delivery guarantee, no redelivery —
and the §6 flow 7 cleanup sweep is a hosted service on a timer whose own note states there is «no
dead-letter queue and nothing to replay». An `events.md` here would describe infrastructure that does
not exist. Revisit at roadmap step 8, when the live-update channel arrives and spec §8 question 1 is
answered — that answer may well introduce one.

**Idempotency-Key — N/A.** No mutating flow in §6 shows a retry note or an async actor on its request
path, so no operation is marked idempotency-key-required.

**Pagination — N/A.** The feature exposes no collection; the cursor page wrapper is unused.

---

## Section A — field origins

One row per `(operation, field)`. Every field in the contract traces to a source, or is named here as
an inference.

| schema_path | origin | confidence |
|---|---|---|
| `registerAccount.request.email` | data-model.md → `AspNetUsers.Email` `nvarchar(256)` | high |
| `registerAccount.request.password` | spec §5 AC-01/AC-02 (8–128 chars). No column — only `PasswordHash` is stored | medium |
| `registerAccount.request.display_name` | data-model.md → `AspNetUsers.DisplayName` `nvarchar(50)` NOT NULL | high |
| `registerAccount.201.id` | data-model.md → `AspNetUsers.Id` `uniqueidentifier`, `Guid.CreateVersion7()` | high |
| `registerAccount.201.email` | data-model.md → `AspNetUsers.Email` | high |
| `registerAccount.201.display_name` | data-model.md → `AspNetUsers.DisplayName` | high |
| `registerAccount.201.Set-Cookie` | data-model.md → `Sessions.Id` (the opaque reference) + sad.md §8 Authentication | high |
| `registerAccount.429.Retry-After` | spec §5 AC-01b («tells them when they may try again») + §6.1 rate limit | high |
| `registerAccount.429.retry_after_seconds` | spec §5 AC-01b. Response-only, no column | medium |
| `createSession.request.email` | data-model.md → `AspNetUsers.Email`. **`format: email` deliberately omitted** — AC-05b forbids a second enumeration oracle | high |
| `createSession.request.password` | spec §5 AC-05. **`minLength` deliberately omitted** — a short password must refuse as `credentials_invalid`, not as a validation error | medium |
| `createSession.201.*` | `$ref` → `Account` (rows above) | high |
| `getCurrentAccount.200.*` | `$ref` → `Account` (rows above) | high |
| `deleteCurrentSession.204` | data-model.md → `Sessions.RevokedAt` (`NULL` means live; set on sign-out) | high |
| `*.X-XSRF-TOKEN` | sad.md §8 «Cross-site request forgery» + ADR 0003 / ADR 0007. **`maxLength: 512` is an inference — no source bounds it** | low |
| `Problem.type` / `.title` / `.status` / `.detail` / `.instance` | architecture-map §Conventions + sad.md §8 — RFC 9457 `ProblemDetails` | high |
| `Problem.code` | neutral `module.error_name` convention, carried as an RFC 9457 extension member | high |

**Deliberately absent from every response** — recorded so their absence reads as a decision, not an
oversight: `PasswordHash` (never a readable form), `AccessFailedCount` and `LastFailedAttemptAt`
(AC-12 — nothing may reveal that an account is under attack), `Sessions.Id` in any body (ADR 0003 —
the reference is httpOnly; exposing it to page scripts defeats the cookie choice), and every unused
Identity default column (`EmailConfirmed`, `PhoneNumber`, `TwoFactorEnabled`, `LockoutEnd`, the six
satellite tables — spec §3 rules their capabilities out of the product).

---

## Section B — drift findings

### Forward — is the contract derived correctly?

**1. Endpoint ↔ data-model** *(core)* — **✓**

| operation | entity read/written |
|---|---|
| `registerAccount` | writes `AspNetUsers` (all four feature columns) + `Sessions` |
| `createSession` | reads `AspNetUsers` via `EmailIndex`; writes `Sessions`, clears `AccessFailedCount` |
| `getCurrentAccount` | reads `Sessions` by PK, then `AspNetUsers`; writes `Sessions.LastSeenAt` (≤ 1×/hour) |
| `deleteCurrentSession` | writes `Sessions.RevokedAt` |

Every endpoint touches ≥ 1 entity; every entity this feature owns is reachable. `DataProtectionKeys`
is touched by no endpoint — correct, it is framework-owned operational state (ADR 0009), not a domain
entity, and exposing it would be the compromise spec §6.1 describes.

**2. Error code ↔ repo error definition** *(core)* — **✓ (recorded, not failed)**

The repository holds no source at this commit — `src/` does not yet exist, and
`src/Uniqua.Projector.Api/ProblemDetailsSetup.cs` is a *target* in architecture-map §Conventions, not
a file. **No error registry found — the eight codes below are the contract's proposal**; `implement`
creates the registry from this table, and `--reconcile` checks it back. This is the reference's
prescribed handling, not a waived check.

| code | status | criterion | §6 branch |
|---|---|---|---|
| `accounts.password_invalid` | 400 | AC-02 | flow 3, inner |
| `accounts.email_invalid` | 400 | AC-02b | flow 3, inner |
| `accounts.email_taken` | 409 | AC-03 | flow 1, first |
| `accounts.display_name_taken` | 409 | AC-11b | flow 1, first |
| `accounts.registration_rate_limited` | 429 | AC-01b | flow 3, first |
| `accounts.credentials_invalid` | 401 | AC-05, AC-05b, AC-12 | flow 4 (branches 1–2), flow 6 |
| `accounts.session_not_recognised` | 401 | AC-07, AC-07b, AC-08, AC-10 | flow 5 (branches 1, expired), flow 2 |
| `accounts.antiforgery_failed` | 403 | *none* — sad.md §8 only | *none* → **OQ-API-1 (closed)** |

All eight match `^[a-z_]+\.[a-z_]+$`. The `accounts.` module prefix follows CONTEXT.md's *account*
glossary term, not a framework idiom.

**3. Validation ↔ constraint** *(core)* — **✓**

| contract | data-model / spec | verdict |
|---|---|---|
| `email maxLength: 256` | `nvarchar(256)` | exact |
| `display_name maxLength: 50, minLength: 1` | `nvarchar(50)` NOT NULL | exact |
| `password minLength: 8, maxLength: 128` (register only) | spec AC-01/AC-02 | exact — AC-derived, no column |
| `id format: uuid` | `uniqueidentifier`, v7 | exact |
| `Problem.code pattern` | neutral convention | exact |

Two **deliberate relaxations** on `createSession`, both of which would otherwise read as drift: no
`format: email` and no `password minLength`. Both are required by AC-05b — a format or length
rejection at sign-in would tell an attacker something a wrong password does not, which is precisely
the second oracle spec §6.1 closes. Recorded here so a future reader does not «tighten» them back.

One asymmetry, resolved by taking the stricter value as the reference directs: `AspNetUsers.Email` is
`NULL`-able (Identity's default, kept per data-model's *[confirmed 2026-09-21]* note), but the
contract makes `email` **required** on both requests and on `Account`. The registration path always
supplies it, so the nullable column is a framework artifact rather than a real optionality — the
stricter contract is correct and the model needs no change.

**4. OpenAPI ↔ sequence** *(supporting)* — **✓**

| §6 flow | operation | branches → responses |
|---|---|---|
| 1 — register unaided | `registerAccount` | `alt` taken → 409 ×2; `else` free → 201 |
| 2 — sign-out withdraws access | `deleteCurrentSession` | happy → 204; AC-10 re-entry → 401 |
| 3 — registration refused | `registerAccount` | rate limit → 429; password → 400; address → 400 |
| 4 — sign in on return | `createSession` | unknown address → 401; wrong password → 401; correct → 201 |
| 5 — recognise on a read | `getCurrentAccount` | absent/revoked → 401; expired → 401; live → 200 |
| 6 — progressive delay | `createSession` | delayed refusal → the *same* 401, deliberately unsignalled |
| 7 — expired-session cleanup | *none* | **accepted** — a hosted service on a timer, no external interface. Not an orphan-sequence flag |

Every `alt`/`else` branch in flows 1–6 has a corresponding response. Flow 7 having no endpoint is
correct by design, not a gap.

### Back-feed — coverage cross-check

**Every §5 AC → ≥ 1 operation/response — ✓ (18/18).**

| AC | mapped to |
|---|---|
| AC-01 | `registerAccount` 201 |
| AC-01b | `registerAccount` 429 |
| AC-02 | `registerAccount` 400 `password_invalid` |
| AC-02b | `registerAccount` 400 `email_invalid` |
| AC-03 | `registerAccount` 409 `email_taken` |
| AC-04 | `createSession` 201 |
| AC-05 | `createSession` 401 |
| AC-05b | `createSession` 401 — same code, same wording, comparable wait |
| AC-06 | `getCurrentAccount` 200 |
| AC-07 | `getCurrentAccount` 401 `session_not_recognised` |
| AC-07b | `getCurrentAccount` 401 — same code |
| AC-08 | `deleteCurrentSession` 204 |
| AC-09 | `deleteCurrentSession` 204 — **outcome stated, mechanism off-contract** (see F-2) |
| AC-10 | `getCurrentAccount` 401 / `deleteCurrentSession` 401 |
| AC-11 | `Account.display_name`, present in every 200/201 body |
| AC-11b | `registerAccount` 409 `display_name_taken` |
| AC-12 | `createSession` 401 — the delay precedes an unchanged refusal |
| AC-13 | `Account.id` — one `Guid.CreateVersion7()`, never reused, never reassigned |

**Every operation → a §4 user story + ≥ 1 AC — ✓ (4/4).**

| operation | user story |
|---|---|
| `registerAccount` | US-01 (register unaided), US-05 (be seen as a person) |
| `createSession` | US-02 (sign in on return), US-06 (protected from guessing) |
| `getCurrentAccount` | US-03 (stay signed in across days), US-07 (keep what I create) |
| `deleteCurrentSession` | US-04 (end my session deliberately) |

**Every §6 `alt`-branch → a response — ✓** (table under point 4).

---

## Open questions and accepted findings

### OQ-API-1 — the antiforgery token has no acquisition path *(closed)*

- **Finding.** `sad.md` §8 requires an antiforgery token on every state-changing request (ADR 0003's
  negative consequence, ADR 0007), and the contract therefore declares `X-XSRF-TOKEN` required on
  `registerAccount`, `createSession` and `deleteCurrentSession`. But **no §6 flow and no §5 AC showed
  how the client obtains one**, and `accounts.antiforgery_failed` was consequently the only error code
  in the contract with no acceptance criterion behind it. Its `maxLength: 512` is likewise an
  inference with no source (Section A, the one `low` row).
- **Classification.** A sequence gap, not an api bug — the hole was upstream.
- **Resolution.** **Closed (review 2026-09-23 (fourth re-review) U-03).** The code already hands out
  the token on every safe response, as a readable `XSRF-TOKEN` cookie
  (`Api/Antiforgery/AntiforgerySetup.cs:13-29,90-105`) — the framework's standard cookie-and-header
  pair, needing no new endpoint. `openapi.yaml` now declares that `Set-Cookie` on `getCurrentAccount`'s
  200 and describes the cookie-and-header pair in `components.parameters.AntiforgeryToken`, whose
  `# unresolved` note is dropped. The `maxLength: 512` inference (Section A) is unaffected and stays
  `low`.

### F-2 — AC-09's mechanism is off-contract *(accepted — pre-existing, not new)*

AC-09 («signing out stops delivery over an already-open live-update connection») is stated in
`deleteCurrentSession`'s description as a guaranteed outcome, but its mechanism has no HTTP surface —
it is the `ISessionRevocationNotifier` port to the realtime hub. This is **already recorded upstream**
as spec §8 open question 1 and by spec §5's own «Verification timing» note, which defers AC-09 to
roadmap step 8. No new action: the contract states the outcome and does not invent the mechanism.

### F-3 — no error registry exists yet *(accepted — recorded under point 2)*

Recorded rather than failed, per the drift-check reference. `implement` materialises the eight codes;
`--reconcile` checks them back.

---

## Section C — recorded deviations from the api-skill defaults

Each is mandated by a source in this repository, confirmed with the owner on 2026-09-21, and is a
**recorded deviation, not drift**.

| Default | Applied instead | Mandated by |
|---|---|---|
| `{code, message, details?}` error envelope | **RFC 9457 `ProblemDetails`** (`application/problem+json`), extended with a `code` member carrying the neutral `module.error_name` convention | architecture-map §Conventions («every failure response is an RFC 9457 `ProblemDetails` produced by one exception handler; endpoints never build an ad-hoc error shape») + sad.md §8 «Error handling» |
| `BearerAuth` global (`http`/`bearer`/JWT) | **`SessionCookie`** — `apiKey` in cookie, opaque `Sessions.Id`, httpOnly/secure/same-site; public operations declare `security: []` | **ADR 0003** (httpOnly cookie, token never reachable from page scripts) + **ADR 0008** (server-side session records) |
| Cursor pagination + page wrapper | none — the feature exposes no collection | n/a |

The `code` extension member is the reconciliation between the two: RFC 9457 permits extension members,
so the repo's mandated error shape is honoured while drift point 2 keeps a stable, machine-readable
anchor for every later feature. Without it the `type` URI would be the only handle and the neutral
snake_case convention would be lost.

---

## Lint

`spectral lint contracts/openapi.yaml --ruleset spectral:oas` → **0 errors, 0 warnings**
(run 2026-09-21; `@stoplight/spectral-cli` via `npx`).

Spectral is not yet wired into a project check target — there is no build to wire it into at this
commit. **Recommendation for `scaffold` / `implement`:** add the lint to the repo's check target
alongside `dotnet test`, so a contract edit that breaks OpenAPI 3.1 fails in CI rather than in review.

## Structural self-check

The bidirectional drift check above **is** this stage's self-check. Result: **3 core points ✓**
(point 2 ✓ with the no-registry finding recorded as the reference prescribes), **1 supporting point
✓**, **1 open question** carried upstream to `sequences` with an owner and a due date, **2 findings
accepted** as pre-existing and already recorded upstream. No core point failed; the run did not need
to pause.
