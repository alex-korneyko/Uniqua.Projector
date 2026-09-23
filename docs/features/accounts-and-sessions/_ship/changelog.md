# Changelog — accounts-and-sessions

## accounts-and-sessions — strangers can register, sign in, stay signed in, and sign out

**What:** A visitor on the public link can now create an account unaided and arrives signed in.
The account has an email address, a password and a unique display name. A returning visitor
signs in with that address and password. The session survives closing the browser, ends after
14 days without a request, and never lasts more than 90 days. Signing out ends only the session
it was sent on; a session on another device stays open. Every visitor who is not signed in sees
the sign-in form first, with a one-click link to registration. Password guessing is slowed by a
delay that grows with each consecutive failure, and is capped per request source. A correct
password is never delayed.

**Why:** Nothing else in the product can be built until an account exists. A board with no one
to own it has nothing to attach to, and the reviewing engineer this project is for cannot get in
at all ([spec](../spec.md) §1–§2). The decisions that carry the weight:

- [ADR-0008](../adr/0008-store-sessions-as-server-side-records.md) — a session is a server-side
  record behind an opaque, httpOnly cookie, not a self-contained ticket. That is what makes
  per-session sign-out and the two expiry limits enforceable.
- [ADR-0009](../adr/0009-keep-the-data-protection-key-ring-in-the-database.md) — the cookie
  protection keys live in the database, so a redeploy does not end every session.
- [ADR-0010](../adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md) — a
  progressive per-account delay plus per-source caps replace account lockout, so a guesser
  cannot lock the owner out.
- [ADR-0006](../adr/0006-build-a-backend-service-and-a-web-frontend.md) and
  [ADR-0007](../adr/0007-deliver-the-web-surface-as-a-client-side-spa.md) — a backend service
  plus a client-side SPA served from the same origin.

The four session rules, each with the file that enforces it and the test that proves it, are in
[`docs/session-rules.md`](../../../session-rules.md).

**How to use** (full contract: [openapi.yaml](../contracts/openapi.yaml)):

| Call | Does |
|---|---|
| `POST /api/v1/accounts` `{ email, password, display_name }` | Registers the account and opens a session (201 + session cookie) |
| `POST /api/v1/sessions` `{ email, password }` | Signs in (201 + session cookie) |
| `GET /api/v1/accounts/me` | Returns the signed-in account, or 401 |
| `DELETE /api/v1/sessions/current` | Signs out of this session only (204) |

State-changing calls need the `X-XSRF-TOKEN` header, echoing the readable `XSRF-TOKEN` cookie that
any `GET` issues. Every refusal is an `application/problem+json` document with a stable `code`.

**Operational notes:**

- **Migrations:** adds four EF Core migrations: `InitialCreate`, `CreateIdentitySchema`,
  `CreateSessions` and `CreateDataProtectionKeys`. They are the repository's first migrations, and
  nothing applies them at startup yet (spec §8, R-24). Apply them explicitly on deploy and on a
  fresh development database:
  `dotnet ef database update --project src/Uniqua.Projector.Infrastructure --startup-project src/Uniqua.Projector.Api`.
- **Config:** outside Development, `TrustedProxies` must list the address of the TLS-terminating
  reverse proxy. If it is empty, the application refuses to start. This is deliberate: without it
  the per-source limits would key on the proxy's own address.
- **Security:** the `DataProtectionKeys` table can mint a session for any account. Grant read
  access only to the identity the application runs as (spec §6.1).
- **Rollback:** revert the deploy, then `dotnet ef database update 0` with the same
  `--project`/`--startup-project`. Each migration's `Down` drops its tables cleanly. This deletes
  every account and session. Nothing depends on these tables yet.

**Known inaccuracies in the published contract at ship** (deferred, spec §8):

- **W-01:** `openapi.yaml` says an `email` longer than 256 characters is refused. The code measures
  the length *after* trimming surrounding whitespace, so a padded value can be accepted.
  sad flow 4 is correct.
- **W-02:** `GET`/`HEAD` on an API path that doesn't accept `GET` (for example
  `GET /api/v1/sessions/current`) falls through to the SPA and returns 200 `text/html`, not the 405
  `api.request_rejected` the contract describes. Wrong non-safe methods do get the 405.
- **W-03:** `contracts/api-sync-report.md` is out of date. `openapi.yaml` is authoritative.

**Acceptance criteria delivered:** AC-01, AC-01b, AC-02, AC-02b, AC-03, AC-04, AC-05, AC-05b,
AC-06, AC-07, AC-07b, AC-08, AC-10, AC-11, AC-11b, AC-12, AC-13.

**Delivered but not yet verifiable:** AC-09 (sign-out withdraws access over an already-open
live-update connection). Its revocation port is built and tested, but the channel arrives at
roadmap step 8. §6 "Session survival across a redeploy" is first checkable at roadmap step 4.
