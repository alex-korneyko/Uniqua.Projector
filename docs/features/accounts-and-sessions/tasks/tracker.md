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
| T26 | Return the stored address from register and sign-in, declare display_name_invalid, and prove the id is stable across the account's life | ports | Alex Korneiko | S | T22, T24 | todo |
| T27 | Move Api registrations into AddAccountsApi, require TrustedProxies outside Development, and prove a trusted proxy's forwarded address is honoured | wiring | Alex Korneiko | S | T24, T26 | todo |
| T28 | Raise the 48-hour cleanup alert as an error log so sad §7 monitoring can fire | infra | Alex Korneiko | S | — | todo |
| T29 | Render a refusal's title as its statement and its detail as the reason | ui | Alex Korneiko | S | — | todo |
| T30 | Present the sign-in form when a known session ends, and treat a 401 from any call as the end of the session | ui | Alex Korneiko | S | — | todo |
| T31 | Keep register, sign-in and sign-out pending until the session is known, and stop truncating passwords | ui | Alex Korneiko | S | T29, T30 | todo |
| T32 | Define the theme tokens the vendored components read | ui | Alex Korneiko | S | — | todo |
| T33 | Type-check tests in their own program and drop vitest globals | config | Alex Korneiko | S | — | todo |
| T34 | Record lightningcss (MPL-2.0, build-time only) as a licence exception | docs | Alex Korneiko | S | — | todo |

**Total:** 34 tasks (T21–T34 are follow-ups from review 2026-09-22), ~16 person-days (12 × M at ~1 day, 8 × S at ~0.5 day).

> **Sizing trigger fired.** sad §11 predicted this: «if `/sdd:tasks` emits more than roughly 12 tasks or more than about 3 days of work, re-run `/sdd:classify-size accounts-and-sessions`». Both thresholds are crossed — 20 tasks and ~16 person-days against a declared size of **M**. The breakdown is honest at that size because the feature carries two surfaces (ADR 0006), a custom authentication handler, three migrations and a background service; the decision on whether to re-size, re-route or split the feature belongs to the owner.
