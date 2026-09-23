---
status: Draft
owner: "Alex Korneiko"
reviewers: ["Alex Korneiko", "Tech Lead"]
updated_at: "2026-09-21"
feature_size: "M"
---

# Test plan — accounts-and-sessions

A stranger must reach a signed-in state unaided, be recognised again across days and devices, and find
that every rule governing that session — how it is carried, how long it lives, what ends it, and what
happens when someone guesses at a password — behaves exactly as the repository says it does.

## Levels

| Level | Scope | Strategy (generic — no tool names) |
|---|---|---|
| Unit | Pure logic the account and the session own: the password and display-name bounds, address normalisation, the expiry rules the `Session` entity owns, the delay curve, the hashing cost, and the rule deciding when the proxy's report of a request source may be trusted. | In-memory, no external dependency; every time-dependent assertion takes its instant from the injected clock port, never from the system clock. |
| Integration | The module against the real store it owns: the unique indexes that make an address and a display name identify exactly one account, the session record and its revocation, the consecutive-failure counter, and the key ring that lets a session outlive a restart. | An ephemeral real dependency — a throwaway database container spun up per suite and torn down after — driven through the same entry point the application uses, with the clock port under the test's control. |
| Component *(web surface)* | The registration and sign-in forms in isolation: what they show on a refusal and what they keep of what was typed. | Render in a component harness against a stubbed transport; assert the rendered output and the surviving field values, with no application boot. |
| Load | The four numeric §6 targets — the two sign-in numbers, the registration number and the recognition number. | The load tool already in the repo, or e.g. k6 or Locust. |
| Contract | <!-- N/A: not selected at the level-confirmation step. contracts/openapi.yaml exists and both participants live in one repository, so the API shape is held by review and by the api stage's own drift report rather than by a test row here. Revisit if the web client is ever built or released separately. --> |
| E2E | <!-- N/A: not selected at the level-confirmation step. The flows an e2e row would have carried (AC-01, AC-04, AC-10) are each covered at integration level through the real entry point, and the one user-visible promise no backend test can see is covered at component level instead. --> |
| Visual-regression | <!-- N/A: no screens.md exists, so there is no approved list of screen states for a baseline to be taken against; a baseline captured now would pin whatever happened to render first. --> |
| E2E-through-UI | <!-- N/A: no ux-flows.md exists, so there is no recorded path for such a test to follow; writing one now would invent the flow rather than derive it. --> |

## AC coverage

| AC (spec.md §5) | Test name (intent-based) | Level | Expected outcome |
|---|---|---|---|
| AC-01 happy path | submitted values within their stated bounds satisfy the account invariants | unit | accepted — a password of 8 and one of 128 characters, and a display name of 50 characters, all pass |
| AC-01 happy path | registration with unused values creates the account and opens a session at once | integration | the account is stored, a live session exists for it, and the display name comes back without a second sign-in |
| AC-01b error | a sixth registration from the same source within a minute is refused | integration | refused, saying plainly that registration is temporarily limited and when it may be tried again; nothing is written |
| AC-01b error | the request source is taken from the proxy's report only when the request came from the proxy | unit | a report arriving from anywhere else is ignored, so the key can be neither forged nor collapsed onto the proxy's own address |
| AC-01b error | the registration form shows the limit and the retry time, keeping what was typed | component | the limit message and the time are rendered, and every field the visitor filled still holds its value |
| AC-02 error | a password shorter than 8 characters is refused | unit | refused, in plain language, naming the 8-character minimum |
| AC-02 error | a password longer than 128 characters is refused | unit | refused at the upper bound as well as the lower one |
| AC-02 error | the password refusal leaves everything else typed in place | component | the reason is rendered and the address and display name still hold what was typed |
| AC-02b error | something that cannot be an email address is refused | unit | refused, saying plainly that the address is not usable |
| AC-02b error | the address refusal leaves everything else typed in place | component | the reason is rendered and the password and display-name fields are not cleared |
| AC-03 domain invariant | a second account on an already-registered address is refused | integration | refused against the real unique index, stating plainly that the address is already registered |
| AC-03 domain invariant | an address is normalised identically at registration and at sign-in | unit | trimming and case folding produce the same comparison key on both paths, so a duplicate is decided by the application and not by a collation |
| AC-04 happy path | the registered address and password open a session | integration | a session is opened and the account's own display name comes back |
| AC-05 error | a wrong password is refused without naming which of the two was wrong | integration | refused with one message that names neither the address nor the password |
| AC-05b error | an unregistered address is refused in the same words as a wrong password | integration | the two refusals are identical, word for word |
| AC-05b error | an unregistered address still costs a full password verification | integration | a dummy credential is verified, so the two refusals take a comparable time and the wait reveals nothing |
| AC-05b error | the sign-in form shows one message for both refusals | component | the same rendered text in both cases, with nothing that distinguishes them |
| AC-06 happy path | a session opened before the instance restarted is still recognised | integration | the session is recognised after a restart, because the key ring lives in the store and not in the process |
| AC-07 domain invariant | a session idle for 14 days is expired | unit | the entity reports it dead one minute past 14 days + 1 hour after the last activity stamp, and live one minute before that boundary — the extra hour is the activity-stamp slack spec §6 allows (sad.md flow 5, `Session.IdleExpiryAfterLastStamp`) |
| AC-07 domain invariant | an idle session is no longer recognised | integration | against a controllable clock, the request is not recognised and the sign-in form is presented |
| AC-07 domain invariant | an ordinary read counts as activity and is stamped at most once an hour | integration | the first read moves last-seen-at, a second read within the hour does not, and the session's life is extended either way |
| AC-07b domain invariant | a session opened 90 days ago is expired however recently it was seen | unit | the entity reports it dead on the absolute ceiling even with last-seen-at set to now |
| AC-07b domain invariant | a steadily used 90-day-old session is no longer recognised | integration | against a controllable clock, the request is not recognised and the sign-in form is presented |
| AC-08 happy path | signing out ends the session it arrived on and only that one | integration | that session is marked revoked; another session the same account holds stays live and is still recognised |
| AC-09 cross-context | signing out notifies the session-revocation port exactly once for that session | integration | the port is called with the session that was revoked, and with no other |
| AC-09 cross-context | a revoked session stops receiving live updates over an already-open connection | integration | **deferred** — per the spec §5 verification-timing note the live-update channel arrives at roadmap step 8 and this row is verified there, not here |
| AC-10 authorization | a request carrying a revoked session cookie is refused | integration | refused and the sign-in form presented, however valid the cookie itself still looks |
| AC-10 authorization | a request carrying an expired session cookie is refused | integration | refused under both expiry rules, decided by the record rather than by the browser having stopped sending it |
| AC-11 happy path | the display name, never the address, is what is shown for an account | integration | the display name comes back wherever the account is shown; the address appears nowhere another member would see |
| AC-11b domain invariant | a second account on an already-used display name is refused | integration | refused against the real unique index, stating plainly that the name is taken |
| AC-11b domain invariant | the taken-name refusal leaves everything else typed in place | component | the reason is rendered and the address and password fields still hold what was typed |
| AC-12 error | the delay grows with the consecutive-failure count | unit | at least 2 seconds at the sixth failure, at least 30 seconds at the tenth, and none at all for a correct password |
| AC-12 error | password verification is deliberately expensive | unit | the hashing parameters cost at least 100 ms per verification (§6), and the framework's own lockout is off (ADR 0010) |
| AC-12 error | a further wrong password after five failures is refused late, and the count survives | integration | the refusal is held back, the failure count and the last-attempt time are persisted, and the message is the same one any refusal uses |
| AC-12 error | the count returns to zero on a correct password and after 15 idle minutes | integration | against a controllable clock both resets happen, and a correct password is accepted with no delay even after nine failures — the owner is never locked out |
| AC-12 error | a 21st failed attempt against one (source, address) pair in the window is refused without verification | integration | refused with `accounts.sign_in_rate_limited` (429) before a password is checked, and the count does not move a 22nd time (review 2026-09-23 P-03) |
| AC-12 error | parallel and abandoned attempts against one pair count toward the cap the same as an awaited one | integration | attempts held open or dropped by the caller still reserve a slot, so a client that never waits for its response exhausts the cap exactly as one that does |
| AC-12 error | the per-(source, address) and per-source caps apply identically to an unregistered address (AC-05b) | integration | an unregistered address refused 20 times from one source is capped the same way a registered one is, before the dummy verification runs |
| AC-05b error | a correct password against another address, or from another source, still succeeds once one pair is capped | integration | the per-address cap on one pair, or the per-source cap on one source, refuses only that pair or that source — a different address, or the same address from an uncapped source, signs in normally |
| AC-12 error | the sign-in form shows the 429 refusal and when to retry | component | the rate-limited message and the retry time are rendered, distinct from the ordinary wrong-password message |
| AC-13 cross-context | an account's identity is stable and never issued twice | integration | the identity is unchanged across sign-out, a run of failed attempts and a fresh sign-in, and no second account is created with an identity already handed out |

## Edge cases / error paths

- Registration rate limit reached, sixth attempt within the minute (AC-01b) → refused, told when to retry, and nothing written — no account, no session, no counter.
- A source report that did not arrive from the reverse proxy (§6.1) → ignored; the limit is keyed to what the proxy itself reports, so it can be neither forged nor collapsed onto one address for the whole world.
- Password at 7, 8, 128 and 129 characters (AC-02) → refused, accepted, accepted, refused.
- Display name at 50 and 51 characters (AC-01) → accepted, refused.
- An address that cannot be an address, and an address differing from a registered one only in case or surrounding whitespace (AC-02b, AC-03) → the first refused as unusable, the second refused as already registered.
- Sign-in with an address no account was ever registered with (AC-05b) → the wrong-password refusal, word for word and in a comparable time.
- A session one minute before 14 days + 1 hour idle (measured from the last activity stamp), and at it; one minute before 90 days old, and at it (AC-07, AC-07b) → live, dead; live, dead. `IsExpired` uses `>=`, so the instant itself is already dead (review 2026-09-23 P-04).
- A 21st failed sign-in against one (source, address) pair within 15 minutes, including one refused outright by an in-flight attempt's reserved slot rather than a completed one (AC-12) → refused before verification, `accounts.sign_in_rate_limited` (429), the count unmoved.
- A 101st failed sign-in from one source across addresses it has tried, none of them individually capped (AC-12) → refused the same way, regardless of which address the 101st names.
- A correct password for a different address than the one capped, or for the same address from an uncapped source (AC-05b, AC-12) → accepted normally; only the capped pair or source is refused.
- Sign-in from a request source that could not be resolved (no `RemoteIpAddress`) (AC-12, §6.1) → neither cap applied, logged as an error; the guessing delay alone still governs.
- A cookie whose session record was never written, and one whose record says revoked (AC-10) → both refused, the sign-in form presented.
- Two sessions of one account, one signed out (AC-08) → one revoked, the other still recognised.
- The store unavailable on an ordinary read (AC-10) → fails closed: the failure view with retry is shown, never the account view. Recognition is a positive assertion; an outage answers neither "recognised" nor "not recognised", so it must never collapse into the sign-in form's ordinary "visitor" refusal, which would misreport a live session as one that was never signed in.

## Test data

- Seed strategy: the builders `data-model.md` § *Test fixtures* already names — `AnAccount()`, `ALiveSession()`, `AnIdleSession()`, `AnAgedSession()`, `ARevokedSession()`, `AnAccountUnderGuessing(failures)` — built beside the integration tests, never as migration rows. Every address is under `example.test`; no real address appears in a fixture.
- Time: every time-dependent fixture and assertion takes its instant from the injected clock port, so the 14-day, 90-day and 15-minute rows are exercised by moving the clock rather than by waiting.
- Integration dependency: an ephemeral real dependency — a throwaway database container per suite, migrated from this feature's own migrations. Not a mocked store: AC-03 and AC-11b are true only because of unique indexes, which a mock does not have.
- Cleanup boundary: per-test. Each test owns the accounts and sessions it created and removes them, because the unique indexes on the address and the display name make leftover rows collide with the next run rather than merely clutter it.

## NFR validation (load)

- p95 sign-in ≤ 600 ms → sustain 10 sign-ins/s for 5 minutes against pre-registered accounts; assert p95 ≤ 600 ms over successful sign-ins only, excluding any attempt held back by the progressive delay.
- p95 registration ≤ 800 ms → sustain registrations spread across distinct request sources at no more than 5 per minute per source, for 5 minutes; assert p95 ≤ 800 ms. The spread is not incidental — a single source trips AC-01b at the sixth request and would measure the rate limit instead of the path.
- p95 recognising a session on an ordinary read ≤ 30 ms → sustain 100 reads/s carrying live session cookies for 5 minutes; assert p95 ≤ 30 ms.
- Throughput ≥ 10 sign-ins/s → sustain 10 sign-ins/s for 10 minutes on the reference machine, the 2-vCPU virtual machine §6 names; assert no error-rate regression. On a CI runner the same scenario counts only as a regression check, never as the measurement, because the runner is not the reference machine.
- Password verification ≥ 100 ms per attempt → not a load scenario: §6 states it is guarded by a unit test over the hashing parameters, and it is covered in the AC-12 rows above.
- 100% of unexpired sessions survive a redeploy → not a load scenario either: §6 makes it a post-deployment check, first verifiable at roadmap step 4 when a real deployment exists. **Deferred**, alongside AC-09.

## CI placement

- On every PR: unit and component — the fast suites, with no container and no clock-driven waiting.
- On every PR where the runner can start a container, otherwise on merge to the main branch: integration. These carry the majority of the acceptance criteria, so they run as early as the runner allows rather than being held back by default.
- On schedule or before a release: load, on the reference machine. The same scenarios may run on a CI runner as a regression check only.
- Verified at their own roadmap step rather than in this feature's pipeline: AC-09 over an already-open connection (step 8) and session survival across a redeploy (step 4).
