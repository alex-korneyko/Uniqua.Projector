---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-21"
feature_size: "M"
ticket: "n/a — roadmap step 2, docs/features/accounts-and-sessions"
---

# 0010 — Replace account lockout with a progressive per-account delay

- **Status:** Accepted
- **Date:** 2026-09-21
- **Deciders:** Alex Korneiko (owner), Claude (facilitator)

## Context

ADR 0003 lists «lockout after repeated attempts» among the positive consequences of choosing ASP.NET Core Identity — the framework provides it out of the box. The spec then went the other way: AC-12 and the §6 row «Progressive delay under guessing» commit to a delay that grows with each failure while a correct password is still accepted immediately, explicitly so that the account never becomes unusable to its owner. Spec §8 recorded the divergence as an open question due «before `/sdd:design`», with no superseding decision written down. This ADR is that decision.

## Decision drivers

- Spec AC-12: guessing becomes futile «without the account ever becoming unusable to its owner».
- Spec §6: 6th consecutive failure delayed ≥ 2 s, 10th ≥ 30 s, a correct password never delayed, the counter returning to zero after 15 minutes with no attempt.
- Spec §1: OWASP's *Blocking Brute Force Attacks* documents account lockout as a denial-of-service vector against a named person and as a username-harvesting oracle.
- Spec §3: there is no password recovery and no address verification, so an account locked out by a stranger has no self-service way back.
- `architecture-map.md`: Identity is already the account store, and it already maintains a consecutive-failure count.

## Considered options

1. **Identity's failure counter with a computed delay, lockout disabled** — keep the counter the framework already maintains, switch off its lockout behaviour, and derive the delay from the count.
2. **A separate failure table owned by this feature** — ignore Identity's counter and track attempts in a table of our own.
3. **Identity's built-in lockout, as ADR 0003 anticipated** — accept the framework default and weaken AC-12 to match.

## Decision outcome

**Chosen:** Option 1. Option 3 is rejected on the driver ADR 0003 did not have in front of it: with no password recovery in scope, a lockout triggered by a stranger is unrecoverable for the owner, which turns a protection into an outage. Option 2 duplicates state the Identity schema already carries, leaving an unused column beside a new table for the same idea. Option 1 changes only what is *done* with the count, not where the count lives.

**This supersedes the «lockout after repeated attempts» consequence recorded in ADR 0003.** The rest of ADR 0003 — Identity plus an httpOnly cookie on one origin — stands; only that one bullet is withdrawn. Spec §8's open question № 5 is closed by this record.

## Consequences

**Positive**
- The owner of an account cannot be locked out by someone else's guessing, which matters more than usual here because there is no recovery path.
- No new table and no new column: the counter is already in the Identity schema, so `data-model` carries nothing extra for this.
- The delay is computed, so the §6 numbers (≥ 2 s at the 6th, ≥ 30 s at the 10th, reset after 15 minutes idle) are directly testable against a controllable clock.

**Negative**
- The framework's lockout must be deliberately switched off. If someone later turns it back on because it looks like the safe default, AC-12 breaks silently — this is worth a test that asserts a locked-looking account still accepts the correct password.
- A 30-second delay holds the request open for 30 seconds, occupying a connection. At this scale that is harmless, but it is a real resource cost and is recorded in §11.
- A per-account counter does not slow an attack spread thinly across many accounts — already accepted as residual risk in spec §6.1.

**Neutral**
- Moving to option 2 later would be additive and would not require a data migration, since the counter would simply start being read from elsewhere.

## Links

- Spec: [[../spec.md]] §5 AC-12, §6, §8
- SAD: [[../sad.md]] §8
- Supersedes a consequence of: [[../../../adr/0003-authenticate-with-identity-and-an-httponly-cookie]]
