---
id: T10
title: "Implement the SignOut use case and declare the session-revocation notifier port"
layer: "app"
deps: ["T6"]
blocks: ["T14"]
acs: ["AC-08", "AC-09", "AC-10"]
files_hint:
  - "src/Uniqua.Projector.Application/Accounts/SignOut.cs"
  - "src/Uniqua.Projector.Application/Accounts/Ports/ISessionRevocationNotifier.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SignOutTests.cs"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "done"
---

# T10 — Implement the SignOut use case and declare the session-revocation notifier port

## Place in the sequence

- **Blocked by:** T6 — session ports · **Blocks:** T14 — sessions endpoints, which supplies the Api-side implementation of the port declared here · **Wave:** 4, in parallel with RegisterAccount and SignIn.
- **Lane:** own lane. It declares `ISessionRevocationNotifier` and unit-tests against a fake; the real implementation lands beside the hub in T14. Nothing existing implements the interface, so this is not a compile-coupled pair.

## Why (user story)

> **As an** account
> **I want** signing out to end this browser's session across every channel at once
> **So that** leaving a shared machine does not leave the product open behind me
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

This task delivers the revocation itself and the announcement that carries it beyond HTTP — the promise spec §1 says "must be built rather than inherited".

## Inlined context

> Signing out has to reach the live-update connections that session holds, but the hub lives in **Api**, and §2 fixes the reference direction as `Api → Application` — so a use case may not call the hub. `SignOut` therefore depends on an Application-declared port, `ISessionRevocationNotifier`, whose implementation sits in Api next to the hub and is registered at startup. This is the identical pattern Infrastructure already uses for the session and account ports: the use case never knows who fulfils them.
>
> — `sad.md §5, Building block view, verbatim` · full text: [sad.md](../sad.md)

> `Api->>App: End the session this request arrived on` → `App->>Infra: Mark that one session revoked` → `App->>Hub: This session has ended (through ISessionRevocationNotifier)` → *note:* The port exists - what it does to an already-open connection is still open, spec section 8 question 1 → `Hub->>Hub: Drops the connections held by that session`
>
> — `sad.md §6, flow 2 «signing out withdraws access over every channel at once», abridged` · full text: [sad.md](../sad.md)

> Sessions the same account holds on other devices are untouched — sign-out is per-session (AC-08, ADR 0008). A later request carrying the revoked cookie is refused because the session record says so, not because the browser stopped sending it (AC-10).
>
> — `sad.md §6, after flow 2, verbatim` · full text: [sad.md](../sad.md)

> The adversarial pass sharpened one commitment in particular: a persistent connection authorises its cookie only when it is opened, so "signing out revokes access everywhere" is a promise that must be built rather than inherited, and it is stated here as a promise precisely so that it cannot quietly not happen.
>
> — `spec.md §1, Context, verbatim` · full text: [spec.md](../spec.md)

> **Verification timing.** AC-09 and the §6 row "Session survival across a redeploy" cannot be exercised while this feature closes: the live-update channel arrives at roadmap step 8 and the first real deployment at step 4. Both are binding commitments stated here and verified at those steps; `plan-tests` records them as deferred rather than as covered.
>
> — `spec.md §5, verification timing note, verbatim` · full text: [spec.md](../spec.md)

> **Hard rule — open question:** how signing out reaches an already-open live-update connection. Resolve before roadmap step 8. ADR 0008 makes it possible — the session is server-side state the hub can consult — but does not implement the notification.
>
> — `sad.md §11, open question row, verbatim` · full text: [sad.md](../sad.md)

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md) · [adr/0008](../adr/0008-store-sessions-as-server-side-records.md)) and follow it. Do not guess.

## Data delta

No schema change. One column is written:

| Column | Type | Constraints | Change |
|---|---|---|---|
| `Sessions.RevokedAt` | `datetimeoffset` | NULL | written once, for exactly the session the request arrived on. The row is **kept**, not deleted, so AC-10 is answered by a record rather than by an absence |

— `data-model.md §Entities, table Sessions, abridged` · full text: [data-model.md](../data-model.md)

## API contract

- `DELETE /api/v1/sessions/current` → `204` (no body) · errors: `401 accounts.session_not_recognised`, `403 accounts.antiforgery_failed`.
- This task owns the use case behind that operation; the endpoint, the cleared cookie and the hub-side notifier are T14's.
- The contract states the AC-09 outcome, not the mechanism: "How that reaches an already-open connection is spec §8 open question 1, due before roadmap step 8."

— `contracts/openapi.yaml, operationId deleteCurrentSession, abridged` · full text: [openapi.yaml](../contracts/openapi.yaml)

## Acceptance criteria

### AC-08 — happy path

> **Given** an account with an active session
> **When** they sign out
> **Then** the system ends the session the sign-out travelled on — and only that one, leaving any session the same account holds on another device untouched — and presents them the view a visitor sees
>
> — `spec.md §5, AC-08, verbatim` · full text: [spec.md](../spec.md)

### AC-09 — cross-context

> **Given** an account that is signed in and is holding an open live-update connection to a board
> **When** they sign out
> **Then** the system stops delivering board updates over that already-open connection, because the end of a session withdraws access over every channel at once and not only over the one the sign-out travelled on
>
> — `spec.md §5, AC-09, verbatim` · full text: [spec.md](../spec.md)

**AC-09 is deferred verification, not deferred work.** No hub and no board exist until roadmap step 8, so what this task must deliver is the port, the call to it on every revocation, and a test that the call happens. The connection actually going quiet is verified at step 8.

### AC-10 — authorization

> **Given** a visitor whose session has ended, by signing out or by expiry
> **When** they attempt to reach anything reserved for a signed-in account
> **Then** the system refuses and presents the sign-in form, regardless of what their browser still holds
>
> — `spec.md §5, AC-10, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] `ISessionRevocationNotifier.SessionRevoked(Guid sessionId)` — `src/Uniqua.Projector.Application/Accounts/Ports/ISessionRevocationNotifier.cs`. One method, no hub type in the signature.
- [ ] `SignOut` use case taking the session id the request arrived on — `src/Uniqua.Projector.Application/Accounts/SignOut.cs`.
- [ ] Revoke through `ISessionStore.Revoke`, then announce through the notifier — in that order, so nothing is announced that is not already a fact in the store.
- [ ] Register a no-op implementation as the default, so the use case is wired and green before the hub exists; T14 replaces it with the real one.
- [ ] Unit test with a fake notifier: it is called exactly once, with the revoked session's id, after the store write.
- [ ] Integration test: signing out of one of two sessions held by the same account leaves the other live.

## Edge cases

| Case | Behaviour |
|---|---|
| The same account holds a session on another device | Untouched — only the id the request arrived on is revoked (AC-08) |
| Sign-out is submitted twice | Idempotent: `RevokedAt` keeps its first value and the notifier is called again harmlessly |
| The notifier throws (hub unavailable) | The revocation still stands — it is already written. The failure is logged and does not turn a completed sign-out into an error |
| The session was already expired when sign-out arrives | Revoked anyway; expiry and revocation are independent facts (T2) |
| No hub exists yet (the state at this feature's close) | The no-op notifier is called; the test asserts the call, which is all AC-09 can be held to before roadmap step 8 |
| A request arrives afterwards with the same cookie | Refused by the record (AC-10) — that refusal is T12's path, not this one's |

## Definition of Done

- [ ] A unit test proves the notifier is called exactly once per revocation, after the store write, with the correct session id.
- [ ] An integration test proves revoking one session leaves the account's other sessions live (AC-08).
- [ ] An integration test proves a request carrying the revoked cookie is no longer recognised (AC-10, through T12's handler).
- [ ] A notifier failure does not fail the sign-out, and is logged with no session reference in the message.
- [ ] `Application` still references no hub type and no Api type.
- [ ] every Hard Rule inlined above still holds
- [ ] lint + vet clean
