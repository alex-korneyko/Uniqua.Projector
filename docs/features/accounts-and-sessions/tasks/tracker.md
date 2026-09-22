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
| T17 | Sign-in screen, one indistinguishable refusal | ui | Alex Korneiko | S | T15 | todo |
| T18 | Three quality-scenario suites + anti-lockout regression | tests | Alex Korneiko | M | T9, T12, T13, T14 | todo |
| T19 | The four session rules, written down | docs | Alex Korneiko | S | T12, T14 | todo |
| T20 | Expired-session cleanup hosted service | infra | Alex Korneiko | S | T4, T6 | done |

**Total:** 20 tasks, ~16 person-days (12 × M at ~1 day, 8 × S at ~0.5 day).

> **Sizing trigger fired.** sad §11 predicted this: «if `/sdd:tasks` emits more than roughly 12 tasks or more than about 3 days of work, re-run `/sdd:classify-size accounts-and-sessions`». Both thresholds are crossed — 20 tasks and ~16 person-days against a declared size of **M**. The breakdown is honest at that size because the feature carries two surfaces (ADR 0006), a custom authentication handler, three migrations and a background service; the decision on whether to re-size, re-route or split the feature belongs to the owner.
