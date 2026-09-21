---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-21"
feature_size: "M"
ticket: "n/a — roadmap step 2, docs/features/accounts-and-sessions"
---

# 0008 — Store sessions as server-side records rather than self-contained cookie tickets

- **Status:** Accepted
- **Date:** 2026-09-21
- **Deciders:** Alex Korneiko (owner), Claude (facilitator)

## Context

ASP.NET Core's cookie authentication is self-contained by default: the authentication ticket is encrypted into the cookie itself and validated with no store read, so ending a session can only mean deleting the browser's copy of the cookie. A copy taken beforehand keeps working until it expires, and nothing server-side knows the session ended. Three acceptance criteria are written against the opposite behaviour, so the default cannot be used as-is.

## Decision drivers

- Spec AC-10: an ended session is refused «regardless of what their browser still holds» — impossible without server-side state.
- Spec AC-09: signing out must silence an already-open live-update connection — the hub has to be able to learn that the session ended.
- Spec AC-08: sign-out ends the session it travelled on **and only that one**, so an account-level security stamp (which revokes every session of the account at once) does not fit either.
- Spec AC-07 / AC-07b: a 14-day sliding window plus a 90-day absolute ceiling, both of which should be enforced where they can be observed and changed, not sealed inside an issued cookie.
- Spec §6: «recognising a session on an ordinary read ≤ 30 ms» — a latency budget that only makes sense if a lookup happens.

## Considered options

1. **Server-side session records** — a row per session (id, account, created, last-seen, revoked); the cookie carries an opaque reference and every authenticated request validates against the row.
2. **Self-contained cookie plus a revocation list** — the ticket stays in the cookie, but each session carries an id and the server keeps a list of the ids revoked before their natural expiry.
3. **Keep the framework default and reopen the acceptance criteria** — sign-out clears the browser's cookie only; AC-09 and AC-10 are weakened to match.

## Decision outcome

**Chosen:** Option 1. Option 3 was rejected because it makes the feature's headline promise — that the rules governing a session are stated and checkable — false in exactly the place the primary reader looks. Option 2 was rejected because the saving is illusory: the revocation list has to survive a restart, so it lives in the database anyway, while the sliding window and the absolute ceiling remain sealed inside the issued cookie where they cannot be inspected or shortened. Option 1 pays one indexed read per authenticated request and gets all four session rules enforced in one observable place.

## Consequences

**Positive**
- AC-08, AC-09 and AC-10 are satisfied literally, and the §6.1 «Session theft» mitigation becomes true rather than aspirational.
- The 14-day sliding window and the 90-day ceiling are server-side facts that a test can manipulate through a controllable clock, which is what the §6 rows already assume.
- A session row is the natural place to hang anything a later feature needs (which device, when last seen), without reissuing cookies.

**Negative**
- One indexed read per authenticated request, against the ≤ 30 ms budget. The read is by primary key, so the budget is comfortable, but it is a real cost the default did not have.
- Expired and revoked rows accumulate and need periodic removal; §7 carries the cleanup.
- Sign-out must reach the live-update connections held by that session. The mechanism is still open (spec §8, row 1) — this decision makes it *possible*, it does not by itself implement it.

**Neutral**
- ASP.NET Core Identity is still used for the account store and password hashing; this decision replaces only how the authentication ticket is validated.


## Links

- Spec: [[../spec.md]]
- SAD: [[../sad.md]] §4
