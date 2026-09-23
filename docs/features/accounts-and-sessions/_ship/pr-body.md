## Summary

Strangers on the public link can now register, arrive signed in, sign in again later, stay signed in across browser restarts, and sign out of exactly one session. Sessions are server-side records behind an httpOnly cookie, bounded to 14 days idle and 90 days absolute. Password guessing gets a progressive per-account delay plus per-source caps instead of lockout. Every later roadmap step resolves against the identity this creates. Spec: [`docs/features/accounts-and-sessions/spec.md`](docs/features/accounts-and-sessions/spec.md) · Changelog: [`_ship/changelog.md`](docs/features/accounts-and-sessions/_ship/changelog.md)

## Acceptance criteria

- AC-01 — a visitor registers unaided and arrives signed in, shown their display name ✓
- AC-01b — the 6th registration from one source within a minute is refused, with when to retry ✓
- AC-02 — a password outside 8–128 characters is refused in plain language ✓
- AC-02b — an unusable email address is refused in plain language ✓
- AC-03 — an already-registered address is refused (one address, one account) ✓
- AC-04 — a returning owner signs in and is shown their display name ✓
- AC-05 — a wrong password gets only "address or password is incorrect" ✓
- AC-05b — an unregistered address gets the same wording in comparable time ✓
- AC-06 — the session survives closing the browser ✓
- AC-07 — a session ends after 14 days without a request ✓
- AC-07b — no session outlives 90 days ✓
- AC-08 — sign-out ends only the session it travelled on ✓
- AC-09 — sign-out withdraws access over an open live-update connection: **port built and tested; end-to-end verification deferred to roadmap step 8** (the channel doesn't exist yet)
- AC-10 — an ended session is refused whatever the browser still holds ✓
- AC-11 — display names are recorded, shown instead of the email, and unique ✓
- AC-11b — a taken display name is refused ✓
- AC-12 — progressive delay after 5 consecutive failures; a correct password is never delayed ✓
- AC-13 — one stable, never-reused account identity (GUID v7) ✓

## Design

- Spec: `docs/features/accounts-and-sessions/spec.md`
- Architecture: `docs/features/accounts-and-sessions/sad.md`
- Decisions: `docs/features/accounts-and-sessions/adr/` (ADR 0006–0010) + `docs/adr/0011` (lightningcss build-time licence exception)
- Data model + migrations: `docs/features/accounts-and-sessions/data-model.md` (`InitialCreate`, `CreateIdentitySchema`, `CreateSessions`, `CreateDataProtectionKeys`)
- API: `docs/features/accounts-and-sessions/contracts/openapi.yaml`
- Session rules, each with its enforcing file and proving test: `docs/session-rules.md`
- Review record: `docs/features/accounts-and-sessions/_review/review-2026-09-23-4.md` (**PASS**, after five re-reviews)

## Tasks (SDD-Task trailers)

<details><summary>83 task commits (T1–T69)</summary>

- `3c56e49` T1 — feat(accounts-and-sessions): declare the Identity account model and its schema migration
- `c3128ad` T2 — feat(accounts-and-sessions): give the Session entity the two expiry rules
- `8b8a07a` T3 — feat(accounts-and-sessions): add the Account invariants and the guessing-delay curve
- `d6d3fb7` T11 — feat(accounts-and-sessions): add the one wording table and the forgery guard
- `70bff93` T4 — feat(accounts-and-sessions): map the Session entity and migrate the sessions table
- `ea9a68d` T5 — feat(accounts-and-sessions): keep the data-protection key ring in the database
- `6f9130c` T7 — feat(accounts-and-sessions): put Identity behind IAccountStore with lockout off
- `81d3b9f` T6 — feat(accounts-and-sessions): declare the session ports and implement them over EF Core
- `43fdd18` T8 — feat(accounts-and-sessions): register an account and open its session in one step
- `2b52cde` T9 — feat(accounts-and-sessions): sign in with the progressive delay and one refusal
- `e96b284` T10 — feat(accounts-and-sessions): sign out and declare the session-revocation port
- `e8311de` T12 — feat(accounts-and-sessions): recognise a session on every request
- `0f68ae2` T13 — feat(accounts-and-sessions): expose POST /api/v1/accounts with its per-source limit
- `0029d0f` T14 — feat(accounts-and-sessions): expose the sign-in and sign-out endpoints
- `2806650` T20 — feat(accounts-and-sessions): sweep expired sessions at startup and once a day
- `11848f7` T15 — feat(accounts-and-sessions): add the web session bootstrap and the signed-in shell
- `b001432` T16 — feat(accounts-and-sessions): build the registration screen with every refusal state
- `4e95227` T17 — feat(accounts-and-sessions): build the sign-in screen with one indistinguishable refusal
- `a25480c` T18 — test(accounts-and-sessions): add the three quality-scenario suites
- `3d611ea` T19 — docs(accounts-and-sessions): write the four session rules where a reader will find them
- `0c7c2e8` T21 — fix(accounts-and-sessions): serve the AC-12 delay from the failure being made, and count it first
- `be7e6dc` T22 — fix(accounts-and-sessions): hold unregistered addresses on the same curve as registered ones
- `3c7fb20` T23 — fix(accounts-and-sessions): keep the session cookie across a browser restart, and never end a session early
- `b369d83` T24 — fix(accounts-and-sessions): count registrations against the per-source limit, not attempts
- `e49fb61` T25 — fix(accounts-and-sessions): register every address the account accepts, and stop misreporting store refusals
- `d2351e1` T26 — fix(accounts-and-sessions): show the stored address from every endpoint and declare display_name_invalid
- `046e868` T27 — fix(accounts-and-sessions): wire the Api layer through AddAccountsApi and refuse to start without a trusted proxy
- `9cc8da9` T28 — fix(accounts-and-sessions): raise the 48-hour cleanup alert that sad §7 asks for
- `c0d942c` T29 — fix(accounts-and-sessions): show a refusal's plain statement before its reason
- `03f5781` T30 — fix(accounts-and-sessions): meet an ended session with the sign-in form, and end it on a 401 from any call
- `dcbcb54` T31 — fix(accounts-and-sessions): keep the account forms honest while a session is being established
- `29ef56f` T32 — fix(accounts-and-sessions): define the theme tokens the vendored components read
- `5e0836e` T33 — chore(accounts-and-sessions): type-check tests in their own program and drop Vitest globals
- `6aa7bdd` T34 — docs(accounts-and-sessions): record lightningcss as the one build-time licence exception
- `894f1c1` T35 — fix(accounts-and-sessions): cap failed sign-ins per source before verification
- `5781a4e` T37 — fix(accounts-and-sessions): seed the failure compare-and-set, report the honest contended count
- `04a87cb` T39 — test(accounts-and-sessions): prove a throwing session reader fails closed
- `0c4cfd5` T41 — fix(accounts-and-sessions): measure cleanup staleness from process start too
- `c97d5e0` T44 — fix(accounts-and-sessions): darken input border and ring tokens past 3:1
- `9ae08ab` T36 — fix(accounts-and-sessions): render the sign-in rate limit refusal, not the generic one
- `2e9b155` T38 — fix(accounts-and-sessions): name both password bounds and cover the contract fully
- `15d77e0` T40 — docs(accounts-and-sessions): record the unknown-address counter limits, the 14 days + 1 hour boundary, and close adr 0003
- `561d942` T42 — test(accounts-and-sessions): prove the registration limit under races and the unexpected-error path
- `5e748d5` T43 — fix(accounts-and-sessions): move focus to the new form on sign-in/register switch
- `5b49a60` T45 — chore(accounts-and-sessions): drop the tracked crash dump and sync every task file with the tracker
- `169494a` T46 — fix(accounts-and-sessions): key the sign-in cap by source and address, not source alone
- `88fb904` T48 — fix(accounts-and-sessions): give a malformed body its declared problem in every environment
- `526ab7a` T48 — fix(accounts-and-sessions): address review of T48
- `d6488e2` T49 — fix(accounts-and-sessions): raise the stale-cleanup alert after a short grace period
- `3708177` T49 — fix(accounts-and-sessions): address review of T49
- `322daa4` T47 — fix(accounts-and-sessions): normalise request sources and bound tracked keys
- `3fff8be` T52 — test(accounts-and-sessions): re-baseline the sign-in throughput regression check
- `03c16ff` T50 — test(accounts-and-sessions): prove the sign-in cap's claims end to end
- `913f931` T53 — docs(accounts-and-sessions): trace the sign-in cap through sad, test-plan, session rules and README
- `2ca3afd` T53 — fix(accounts-and-sessions): address review of T53
- `ee16385` T51 — test(accounts-and-sessions): make the contention test, test clock and test peer addresses deterministic
- `f5f2f6c` T54 — fix(accounts-and-sessions): bound the sign-in cap's keys and refuse over-long credentials before reserving
- `452e77e` T58 — fix(accounts-and-sessions): answer a non-400 bad request with its own 4xx, rename the shared 500 to api.unexpected
- `690d8c3` T58 — fix(accounts-and-sessions): address review of T58
- `5666e0a` T59 — test(accounts-and-sessions): prove ExecuteAsync's own grace-deadline reschedule
- `a8c1231` T55 — fix(accounts-and-sessions): throttle the tracked-key ceiling path
- `6232611` T55 — fix(accounts-and-sessions): address review of T55
- `2dc9b85` T57 — fix(accounts-and-sessions): name which cap refused a sign-in in the log line
- `4aed424` T60 — chore(accounts-and-sessions): anchor .gitignore build-output patterns
- `11c1dbe` T56 — test(accounts-and-sessions): prove the tracked-key ceiling's fail-open contract
- `b70356d` T56 — fix(accounts-and-sessions): address review of T56
- `7c97bb6` T61 — docs(accounts-and-sessions): true up the bounded, re-keyed sign-in cap docs
- `3cc040a` T61 — fix(accounts-and-sessions): address review of T61
- `919b72e` T64 — fix(accounts-and-sessions): name the ceiling limiter and survive a backwards clock step
- `962d28a` T64 — fix(accounts-and-sessions): address review of T64
- `248212c` T62 — fix(accounts-and-sessions): trim the email before the sign-in length guard
- `5183b01` T62 — fix(accounts-and-sessions): address review of T62
- `66991d8` T67 — test(accounts-and-sessions): give the Kestrel 413 fixture its own throwaway certificate
- `a8d86a5` T67 — fix(accounts-and-sessions): address review of T67
- `d2b209c` T68 — chore(accounts-and-sessions): drop unanchored gitignore build patterns
- `0a61013` T66 — test(accounts-and-sessions): prove a run of 429s does not keep AC-12's count alive
- `ed91301` T65 — test(accounts-and-sessions): prove the limiter release paths hold under mutation
- `4899bd6` T65 — fix(accounts-and-sessions): address review of T65
- `e15a5d7` T63 — fix(accounts-and-sessions): refuse request bodies with unknown fields
- `231c837` T63 — fix(accounts-and-sessions): address review of T63
- `1185b3f` T69 — test(accounts-and-sessions): check README for the corrected reset wording
- `e1f22d7` T69 — docs(accounts-and-sessions): true up reset wording and the contract to code
- `4707f97` T69 — fix(accounts-and-sessions): address review of T69

</details>

## Verification

- Build: `dotnet build` — 0 warnings, 0 errors
- Unit (Domain): 63/63
- Integration (SQL Server container via `WebApplicationFactory`): 317 passed, 1 skipped (the redeploy scenario, deferred to roadmap step 4), 0 failed
- Format + lint: `dotnet format --verify-no-changes` exit 0 · `oxlint` exit 0
- Web: `tsc -b` + Vite build passes · Vitest 61/61
- **Ran the feature:** booted the API against a fresh SQL Server 2022 container with the migrations applied, then drove it over HTTPS with separate cookie jars:
  - AC-01: register → 201, and `/me` returns `display_name` "Alice …". The cookie is `httponly; secure; samesite=lax; max-age=7776000`, so it persists across a browser restart (AC-06).
  - AC-03 / AC-11b: the same address with different case and a trailing space → 409 `accounts.email_taken`; the same display name → 409 `accounts.display_name_taken`.
  - AC-02 / AC-02b: a 3-character password → 400 `accounts.password_invalid`; `not-an-address` → 400 `accounts.email_invalid`.
  - AC-05 / AC-05b: a wrong password and an unregistered address each return an identical 401 body, in 0.283 s and 0.288 s.
  - AC-08 / AC-10: signed in on a second jar, signed out on the first → 204. The first jar is then 401, the second is still 200, and replaying the first jar's pre-sign-out cookie gives 401.
  - AC-12: failures 1–5 took about 0.3 s each, the 6th 2.27 s and the 7th 4.25 s. The correct password was then accepted in 0.22 s.
  - AC-01b: the 6th registration from one source inside a minute → 429 `accounts.registration_rate_limited`, with `Retry-After: 44`.
  - CSRF: a state-changing call without `X-XSRF-TOKEN` → 403 `accounts.antiforgery_failed`.
  - Rollback: `dotnet ef database update 0` reverted all four migrations cleanly, and re-applying them succeeded.
- Not exercised live: AC-07 / AC-07b (14-day and 90-day expiry) need a controllable clock, so they are covered only by integration tests. AC-09 and redeploy survival are deferred, as noted above.

## Known contract inaccuracies at ship

These were deferred in spec §8 with an owner and due date (review W-01/W-02):

- `openapi.yaml` says an `email` longer than 256 characters is refused. The code measures the length after trimming, so a padded value is accepted.
- `GET`/`HEAD` on an API path that doesn't take `GET` (e.g. `GET /api/v1/sessions/current`) returns 200 `text/html` from the SPA fallback, not the documented 405. I observed this live.

## Operational notes

- **Migrations:** four new EF Core migrations. Nothing applies them at startup yet (spec §8, R-24), so run `dotnet ef database update --project src/Uniqua.Projector.Infrastructure --startup-project src/Uniqua.Projector.Api` on deploy. **Rollback:** revert the deploy, then run `dotnet ef database update 0`. This drops every account and session; nothing else depends on those tables yet.
- **Config:** outside Development, `TrustedProxies` must name the TLS-terminating reverse proxy. If it's empty, the app refuses to start.
- **Security:** `DataProtectionKeys` can mint a session for any account. Only the app's own identity should be able to read it.

🤖 Generated with [Claude Code](https://claude.com/claude-code)
