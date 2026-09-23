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
- **Amendment 2026-09-22 (review R-05): the delay is capped at 5 minutes.** AC-12's "grows with each additional failure" holds up to the 14th consecutive failure, where the doubling curve reaches the ceiling and stays there. The cap is deliberate: an unbounded delay would let a guesser hold a request open indefinitely, turning the defence into a way of tying up the server. A guesser gains nothing from the cap, since at five minutes per attempt guessing is already futile. It is recorded in spec §6 alongside the two floors.
- **Amendment 2026-09-22 (re-review N-01): a cap on failed sign-ins per request source, counted before verification.** The delay above only slows a client that waits for its own response. A client that hangs up as soon as it has paid the ~100 ms verification cost — the delay is applied *after* that, on the failure the verification already established — learns whether the address and password matched without ever sitting through the wait, and several such clients running in parallel each pay their own wait independently rather than one another's, so throughput scales with how many run at once. AC-12's "guessing becomes futile" is not met against a client shaped this way, however high the per-attempt delay climbs. The fix is a second, independent guard: `SignInRateLimit` reserves a slot for the request source *before* `SignIn` verifies anything, and releases it only when the sign-in succeeds — so a hung-up, cancelled or merely unlucky-losing-the-race attempt still counts, exactly like a completed one. Once a source holds **20 failed sign-ins in a 15-minute sliding window** (`SignInRateLimit.PermittedFailuresPerWindow` / `.Window`), its next attempt is refused with `accounts.sign_in_rate_limited` (429 + `Retry-After`) without a password ever being checked. The cap is keyed by request source exactly as the registration limit is (spec §6.1), so it applies identically whether the submitted address is registered or not (AC-05b) — it adds no second oracle. It shares its sliding-window bookkeeping with `RegistrationRateLimit` through the extracted `SlidingWindowLimiter`, differing only in which outcome releases the slot: registration releases on failure (a refused registration created nothing); sign-in releases on success (a correct password is never held against the cap, from any source — AC-12 stands). **Residual risk, recorded in spec §6.1:** the cap counts per source, not per account, so one account attacked from many request sources is not slowed by it — the same shape of risk the registration limit already accepts.
- **Amendment 2026-09-23 (second re-review P-01): the per-source cap is re-keyed to (source, address), with a looser per-source ceiling, and skipped for an unresolved source.** The amendment above keyed `SignInRateLimit` by request source alone, so once a source held 20 failed sign-ins, the *next* attempt from it was refused with 429 before a password was even checked — including a correct one, for any address. That directly contradicted AC-12's "a correct password is still accepted immediately … without the account ever becoming unusable to its owner": a guesser sharing the owner's address — an office NAT, a CGNAT, a proxy missing from `KnownProxies`, or the single `"unknown"` key when `RemoteIpAddress` was null — could block the owner's own correct password for up to 15 minutes. The fix keys the per-address slot by **(request source, normalised email address)** — the same normalisation `IdentityAccountStore` applies — so a source that has capped one address still admits a correct password for a different one; a separate, looser **per-source ceiling of 100 failed sign-ins across every address** (`SignInRateLimit.PermittedFailuresPerSourceWindow`) still bounds a guesser who rotates across addresses from one source to route around the per-address cap. When the request source resolves to `RequestSource.Unknown`, neither cap is applied at all, logged as a warning: keying either cap on the literal string `"unknown"` would give every caller with no resolvable address one shared budget, which is worse than not capping them. AC-12's "never unusable to its owner" is read from here on as "never by anyone who does not share the owner's request source" — spec §5 states the clarification directly. **Residual risk, recorded in spec §6.1:** a guesser who shares the owner's request source and targets the owner's own address specifically, or a source that has reached its own per-source ceiling, can still refuse the owner for up to 15 minutes; this is narrower than the amendment above, which reached a correct password for *any* address from a capped source.
- Moving to option 2 later would be additive and would not require a data migration, since the counter would simply start being read from elsewhere.
- **Amendment 2026-09-22 (re-review N-03): the unregistered-address counter's limits are accepted residual risk.** AC-05b's comparable-time guarantee for an address no account was ever registered with is held by an in-process counter (`IUnknownAddressAttempts`, sad §5) that drives the same delay curve as a registered account under guessing. It carries two limits, previously stated only in a code comment (`Infrastructure/Accounts/InMemoryUnknownAddressAttempts.cs`): the count is held in memory, so a restart clears every entry and an unregistered address answers faster than a registered one already under guessing for the next few attempts after a restart; and past 100,000 distinct addresses tracked inside one reset window (`Capacity`), a further new address goes untracked and answers at the fastest, unslowed rate. Both are accepted rather than fixed, for the same reason option 2 above was rejected: a durable, unbounded-capacity counter would duplicate state for a guard whose purpose is already served, at this scale, by the registration-form enumeration spec §6.1 accepts. Recorded in spec §6.1.

## Links

- Spec: [[../spec.md]] §5 AC-12, §6, §8
- SAD: [[../sad.md]] §8
- Supersedes a consequence of: [[../../../adr/0003-authenticate-with-identity-and-an-httponly-cookie]]
