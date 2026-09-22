# Tracker — accounts-and-sessions

> Status of every task in the epic. `implement` updates `done` as it commits each task.
> States: `todo` · `in_progress` · `blocked` · `review` · `done`.

| # | Task | Layer | Owner | Estimate | Blocked by | Status |
|---|---|---|---|---|---|---|
| T1 | Identity account model + identity-schema migration | migration | Alex Korneiko | M | — | done |
| T2 | Session entity owning the 14-day and 90-day rules | domain | Alex Korneiko | S | — | done |
| T3 | Account entity, sentinel errors, progressive-delay rule | domain | Alex Korneiko | M | — | done |
| T4 | Sessions table migration with its three indexes | migration | Alex Korneiko | S | T1, T2 | done |
| T5 | Data-protection key-ring migration | migration | Alex Korneiko | S | T4 | done |
| T6 | Session ports + EF Core implementation | infra | Alex Korneiko | M | T2, T4 | done |
| T7 | Identity behind IAccountStore, lockout off, hashing tuned | infra | Alex Korneiko | M | T1, T3 | done |
| T8 | RegisterAccount use case | app | Alex Korneiko | M | T3, T6, T7 | done |
| T9 | SignIn use case with the progressive delay | app | Alex Korneiko | M | T3, T6, T7 | done |
| T10 | SignOut use case + revocation port | app | Alex Korneiko | S | T6 | done |
| T11 | ProblemDetails handler + antiforgery | wiring | Alex Korneiko | S | — | done |
| T12 | Session recognition handler, cookie, key ring, `/me` | ports | Alex Korneiko | M | T2, T5, T6, T11 | done |
| T13 | `POST /api/v1/accounts` + registration rate limit | ports | Alex Korneiko | M | T8, T11, T12 | done |
| T14 | Sign-in / sign-out endpoints + hub notifier | ports | Alex Korneiko | M | T9, T10, T11, T12 | done |
| T15 | Web API client, session bootstrap, signed-in shell | ui | Alex Korneiko | M | — | done |
| T16 | Registration screen, all six states | ui | Alex Korneiko | M | T15 | done |
| T17 | Sign-in screen, one indistinguishable refusal | ui | Alex Korneiko | S | T15 | done |
| T18 | Three quality-scenario suites + anti-lockout regression | tests | Alex Korneiko | M | T9, T12, T13, T14 | done |
| T19 | The four session rules, written down | docs | Alex Korneiko | S | T12, T14 | done |
| T20 | Expired-session cleanup hosted service | infra | Alex Korneiko | S | T4, T6 | done |
| T21 | Serve the AC-12 delay from the attempt being made, reset the stored count after the quiet period, and record a failure before waiting | app | Alex Korneiko | S | — | done |
| T22 | Hold refusals for unregistered addresses on the same curve, so the wait does not reveal registration | app | Alex Korneiko | S | T21 | done |
| T23 | Make the session cookie persistent and add the activity-stamp slack after the 14 days, not before | domain | Alex Korneiko | S | — | done |
| T24 | Count only accepted registrations against the per-source limit and prune stale sources | ports | Alex Korneiko | S | — | done |
| T25 | Accept every address MailAddress accepts and stop mapping unexpected Identity errors to a uniqueness refusal | infra | Alex Korneiko | S | T21 | done |
| T26 | Return the stored address from register and sign-in, declare display_name_invalid, and prove the id is stable across the account's life | ports | Alex Korneiko | S | T22, T24 | done |
| T27 | Move Api registrations into AddAccountsApi, require TrustedProxies outside Development, and prove a trusted proxy's forwarded address is honoured | wiring | Alex Korneiko | S | T24, T26 | done |
| T28 | Raise the 48-hour cleanup alert as an error log so sad §7 monitoring can fire | infra | Alex Korneiko | S | — | done |
| T29 | Render a refusal's title as its statement and its detail as the reason | ui | Alex Korneiko | S | — | done |
| T30 | Present the sign-in form when a known session ends, and treat a 401 from any call as the end of the session | ui | Alex Korneiko | S | — | done |
| T31 | Keep register, sign-in and sign-out pending until the session is known, and stop truncating passwords | ui | Alex Korneiko | S | T29, T30 | done |
| T32 | Define the theme tokens the vendored components read | ui | Alex Korneiko | S | — | done |
| T33 | Type-check tests in their own program and drop vitest globals | config | Alex Korneiko | S | — | done |
| T34 | Record lightningcss (MPL-2.0, build-time only) as a licence exception | docs | Alex Korneiko | S | — | done |
| T35 | Cap failed sign-ins per request source, counted before the verification, so hanging up no longer escapes the AC-12 delay | ports | Alex Korneiko | S | — | done |
| T36 | Render the sign-in-rate-limited refusal on the sign-in screen with its statement and wait | ui | Alex Korneiko | S | T35 | todo |
| T37 | Seed the failure compare-and-set with the count already read, and return the count actually written under contention | infra | Alex Korneiko | S | — | done |
| T38 | Name both password bounds, lock every declared problem in the contract test, and give a malformed body a declared problem | ports | Alex Korneiko | S | T35 | todo |
| T39 | Prove an unavailable session store fails closed, and word the test-plan row as the UI behaves | tests | Alex Korneiko | S | — | done |
| T40 | Record the unknown-address counter limits, the 14 days + 1 hour boundary, and close the ADR 0003 question | docs | Alex Korneiko | S | T35, T39 | todo |
| T41 | Measure cleanup staleness from the last success or the process start, so one failed startup sweep raises no alert | infra | Alex Korneiko | S | — | done |
| T42 | Prove the registration limit holds under parallel submissions, and that an unexpected Identity error is not reported as a uniqueness refusal | tests | Alex Korneiko | S | T35, T37, T38 | todo |
| T43 | Move keyboard focus to the new form when switching between sign-in and registration | ui | Alex Korneiko | S | T36 | todo |
| T44 | Darken the input border and focus-ring tokens to at least 3:1 against the background | ui | Alex Korneiko | S | — | done |
| T45 | Remove the tracked crash dump and bring every task file status in line with the tracker | config | Alex Korneiko | S | T35, T36, T37, T38, T39, T40, T41, T42, T43, T44 | todo |

**Total:** 45 tasks (T21–T34 are follow-ups from review 2026-09-22; T35–T45 from its re-review), ~16 person-days (12 × M at ~1 day, 8 × S at ~0.5 day).

> **Sizing trigger fired.** sad §11 predicted this: «if `/sdd:tasks` emits more than roughly 12 tasks or more than about 3 days of work, re-run `/sdd:classify-size accounts-and-sessions`». Both thresholds are crossed — 20 tasks and ~16 person-days against a declared size of **M**. The breakdown is honest at that size because the feature carries two surfaces (ADR 0006), a custom authentication handler, three migrations and a background service; the decision on whether to re-size, re-route or split the feature belongs to the owner.
