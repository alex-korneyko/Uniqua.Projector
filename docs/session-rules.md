# Session rules

Four rules govern a session here: how it is carried, how long it lives, what ends it, and what
happens when someone guesses at a password. Each is written below with the file that enforces it,
the test that proves it, and a way to check it yourself from outside the application.

That last part is the point. No product in this category publishes any of this, so a reader cannot
learn it by clicking and has to be handed it — and a written rule nobody can verify is only a claim.
If anything below disagrees with the code, **the code is right and this document is a defect**.

For the reasoning behind each decision, follow the ADR link. This page stays short on purpose.

---

## 1. How a session is carried

A cookie named `projector_session`, holding an **opaque reference** to a session record in the
database — not the session's identifier, and not any information about the account.

The cookie is `HttpOnly`, `Secure`, `SameSite=Lax` and scoped to `/`. No page script can read it, and
no endpoint returns it in a response body. The reference is encrypted with a key from the database,
so a cookie is only meaningful to an application holding that key.

Because the browser attaches the cookie automatically, it is never on its own proof that the account
*meant* to make a request: every state-changing request additionally carries an antiforgery token
this application issued.

- **Enforced by** [`src/Uniqua.Projector.Api/Accounts/SessionCookie.cs`](../src/Uniqua.Projector.Api/Accounts/SessionCookie.cs)
  and [`src/Uniqua.Projector.Api/Accounts/SessionAuthenticationHandler.cs`](../src/Uniqua.Projector.Api/Accounts/SessionAuthenticationHandler.cs)
- **Proved by** [`tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionRecognitionTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionRecognitionTests.cs)
- **Check it yourself:** sign in, then open your browser's cookie inspector. `projector_session` is
  marked HttpOnly and Secure, and its value is opaque — it appears nowhere in any response body. In
  the JavaScript console, `document.cookie` does not contain it.
- **Why:** [ADR 0003](adr/0003-authenticate-with-identity-and-an-httponly-cookie.md)
  (cookie over token) and [ADR 0008](features/accounts-and-sessions/adr/0008-store-sessions-as-server-side-records.md)
  (a server-side record over a self-contained ticket).

## 2. How long it lives

Two limits, and whichever is reached first ends the session:

- **14 days** from the last request made on it. Any request counts, including one that only reads.
- **90 days** from when it was opened — however actively it has been used in the meantime.

The first limit slides, so someone who keeps coming back is never signed out for it. The second does
not, so no session can live indefinitely by being kept warm. Activity is recorded at most once an
hour, which means a session can end up to an hour later than the first limit implies; that slack is
deliberate and is the only imprecision here.

- **Enforced by** [`src/Uniqua.Projector.Domain/Accounts/Session.cs`](../src/Uniqua.Projector.Domain/Accounts/Session.cs)
  — `IsExpired` is the only place in the repository where either figure appears as a rule.
- **Proved by** [`tests/Uniqua.Projector.Api.IntegrationTests/Quality/SessionLifetimeTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Quality/SessionLifetimeTests.cs)
  and, at the boundaries, [`tests/Uniqua.Projector.Domain.Tests/Accounts/SessionTests.cs`](../tests/Uniqua.Projector.Domain.Tests/Accounts/SessionTests.cs)
- **Check it yourself:** sign in and leave the tab alone. `GET /api/v1/accounts/me` answers `200`
  while the session is live and `401` once either limit has passed. You do not have to wait 14 days
  to see the rule — `grep -rn "IdleLifetime\|AbsoluteLifetime" src/` finds every use of both figures,
  and there is only one definition of each.

## 3. What ends it

Four things, and nothing else:

- **Signing out**, which ends exactly the session the sign-out travelled on. A session the same
  account holds on another device is untouched.
- **Either time limit** above.
- **The 90-day ceiling**, even mid-use.
- Nothing else. In particular: **a redeploy ends no session.** The key material that protects the
  cookie lives in the database rather than in the running process, so replacing the container,
  moving to another machine or restoring from backup all carry it along.

A session that has ended is refused by **the record**, not by the browser having forgotten the
cookie. That distinction matters: a request arriving with a perfectly intact cookie for an ended
session is still refused, and all the ways of being unrecognised — no cookie, an unreadable cookie,
an unknown session, a revoked one, an expired one — return one identical response, so nothing can be
learned from the difference.

- **Enforced by** [`src/Uniqua.Projector.Application/Accounts/SignOut.cs`](../src/Uniqua.Projector.Application/Accounts/SignOut.cs)
  and [`src/Uniqua.Projector.Infrastructure/DependencyInjection.cs`](../src/Uniqua.Projector.Infrastructure/DependencyInjection.cs)
  (the key ring)
- **Proved by** [`tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs)
  and [`tests/Uniqua.Projector.Api.IntegrationTests/Accounts/DataProtectionKeyRingTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Accounts/DataProtectionKeyRingTests.cs)
- **Check it yourself:** sign in on two browsers. Sign out of one; the other keeps working. Then
  copy the signed-out browser's cookie value and replay it with `curl`: still refused, byte for byte
  the same refusal an unknown cookie gets.
- **Why:** [ADR 0008](features/accounts-and-sessions/adr/0008-store-sessions-as-server-side-records.md)
  and [ADR 0009](features/accounts-and-sessions/adr/0009-keep-the-data-protection-key-ring-in-the-database.md).

### Two things here are not verified yet

Both are stated as commitments with the step that will verify them, rather than as claims:

- **A revoked session stops receiving live updates over a connection that is already open.** There
  is no live-update channel yet; it arrives at **roadmap step 8**, and the check belongs there. What
  works today is that the revocation is announced through a port the moment it is recorded.
- **A session surviving a real redeploy.** The mechanism is tested — a second application built over
  the same database reads the first one's keys and accepts its cookies — but a genuine deployment to
  redeploy does not exist until **roadmap step 4**. Until then the test for it is present and
  explicitly skipped, so the gap shows up in every test run.

## 4. What happens when someone guesses at a password

An attempt against an account is held back for longer with each consecutive failure, and the account
is **never locked** — there is no state from which the owner has to be unlocked by anyone. That is
not quite "no cap, ever", though: past a point a guesser is refused outright rather than merely
delayed, and the two caps below exist precisely so the delay does not have to hold back a client that
does not wait for its own response.

- The first five consecutive failures are not delayed at all. Mistyping your own password a few times
  costs you nothing.
- From the 6th the delay doubles: **at least 2 seconds at the 6th**, at least 32 at the 10th, up to a
  ceiling of five minutes.
- The count returns to zero on a correct password, or after **15 minutes** in which no attempt is
  made at all.
- **A correct password is never delayed or capped**, however hard anyone else has been guessing.
  Lockout — a state that only an out-of-band action clears — was rejected in favour of this delay,
  and the caps below never add one back: both release on the next 15-minute window regardless of
  what happens in it.

On top of the delay, two caps bound a client that hangs up as soon as it has verified whether an
address is registered, so it pays none of the delay above: past **20 failed sign-ins or attempts
still in flight** for one (request source, address) pair in 15 minutes, the next attempt against that
pair is refused with **`429`** before a password is even checked; past **100 failed** sign-ins from
one request source across every address it has tried in the same window, the next attempt from that
source is refused regardless of which address it names. A correct password releases both slots at once, from
any source. Both are keyed by request source (in practice, the caller's IP address) — a guesser who
shares the owner's own request source, or whose source has separately reached its own 100-failure
ceiling, can still refuse the owner for up to 15 minutes; that residual is accepted and recorded in
[spec §6.1](features/accounts-and-sessions/spec.md).

Nothing in a response reveals that any of this is happening. A refusal that waited thirty seconds is
identical, field for field and header for header, to one that waited nothing — otherwise the delay
itself would tell a guesser they had found a real account. The `429` from a cap is a different code
from the `401` a delayed or undelayed refusal returns, but both are returned identically whether or
not the address is registered.

Relatedly: a wrong password and an address no account was ever registered with are refused in the
same words and take a comparable time, because the unregistered case still performs a full password
verification. Neither the message nor the wait reveals whether an address is registered.

- **Enforced by** [`src/Uniqua.Projector.Domain/Accounts/GuessingDelay.cs`](../src/Uniqua.Projector.Domain/Accounts/GuessingDelay.cs),
  [`src/Uniqua.Projector.Application/Accounts/SignIn.cs`](../src/Uniqua.Projector.Application/Accounts/SignIn.cs)
  and, for the two caps, [`src/Uniqua.Projector.Api/Accounts/SignInRateLimit.cs`](../src/Uniqua.Projector.Api/Accounts/SignInRateLimit.cs)
- **Proved by** [`tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs`](../tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs)
  and [`tests/Uniqua.Projector.Domain.Tests/Accounts/GuessingDelayTests.cs`](../tests/Uniqua.Projector.Domain.Tests/Accounts/GuessingDelayTests.cs)
- **Check it yourself:** submit a wrong password to `POST /api/v1/sessions` seven times in a row and
  time the responses; the later ones take seconds. Then submit the correct password: it is accepted
  immediately. Compare the response bodies of a delayed refusal and an undelayed one — they are the
  same.
- **Why:** [ADR 0010](features/accounts-and-sessions/adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md).

---

## What this deliberately does not do

None of the following exists yet. They are out of scope rather than overlooked:

- **No password recovery or reset.** Someone who forgets their password cannot currently regain the
  account.
- **No email address verification.** An address is not confirmed to belong to whoever typed it.
- **No third-party sign-in.** No Google, Microsoft or similar.
- **No account removal or deactivation.** The schema supports it; no path in the product does.

## The contract

The endpoints, their request and response shapes, and every refusal code are in
[`docs/features/accounts-and-sessions/contracts/openapi.yaml`](features/accounts-and-sessions/contracts/openapi.yaml),
which is the contract of record. Every failure this application returns is an RFC 9457
`application/problem+json` document carrying a stable `code`; the wording of all of them is in one
file, [`src/Uniqua.Projector.Api/AccountProblems.cs`](../src/Uniqua.Projector.Api/AccountProblems.cs).
