---
status: Accepted
owner: "Alex Korneiko"
reviewers: []
updated_at: "2026-09-20"
feature_size: ""
ticket: "n/a — foundational decision from the survey session"
---

# 0003 — Authenticate with ASP.NET Core Identity and an httpOnly session cookie on a single origin

- **Status:** Accepted
- **Date:** 2026-09-20
- **Deciders:** Alex Korneiko (owner), Claude (facilitator)

## Context

The client and the API are separate applications, and a persistent push channel sits alongside the HTTP
API. Both have to be authenticated: without authorization on the channel a non-member could subscribe to
someone else's board and watch everything that happens on it. How the browser proves identity also decides
whether the client and the API may be deployed separately, so it has to be settled before the week-2 deploy.

## Decision drivers

- Access control is what the primary reader probes first, and the standard question about a project of this
  shape is where the token is kept (`idea-brief.md` §3, §6).
- An invited member must be refused everything a member may see, over both transports (`idea-brief.md` §6).
- Registration by an unfamiliar person has to work unattended (`idea-brief.md` §2).

## Considered options

1. **ASP.NET Core Identity + httpOnly cookie, client and API on one origin** — the token is never reachable
   from page scripts.
2. **ASP.NET Core Identity + bearer tokens held by the client** — deployment-flexible, token exposed to scripts.
3. **An external identity provider** — accounts, passwords and social sign-in delegated to a third party.
4. **A hand-rolled scheme** — own user table, own password hashing, own token issuance.

## Decision outcome

**Chosen:** Option 1. The cookie is set with the httpOnly flag, so page scripts cannot read it and a
cross-site scripting hole does not hand over the session. The push channel needs no separate authorization
mechanism, because the browser attaches the same cookie when it opens the connection. Option 4 was rejected
as a reliable negative signal to the intended reader; option 3 would have removed the part of the project
the owner wants to learn.

## Consequences

**Positive**
- The most defensible possible answer to the question this project will actually be asked.
- One authentication mechanism covers both the HTTP API and the push channel, with no second code path.
- Identity brings password hashing, lockout after repeated attempts and password reset without custom code.

**Negative**
- The client and the API must share an origin, so the API serves the built client and the tempting
  "front end on one free host, API on another" deployment is off the table. This narrows the week-2
  hosting options further, on top of the constraint the store choice already imposes.
- Cookie authentication needs correct same-site and secure attributes and cross-site request protection on
  state-changing endpoints — details that are easy to get subtly wrong.

**Neutral**
- Moving to bearer tokens later is possible and mostly affects the client and the channel handshake; the
  account store itself would not change.

## Links

- Idea brief: [[../idea-brief.md]] §3, §6
- Architecture map: [[../architecture-map.md]] §C4, §Constraints
- Related ADR: [[0004-push-board-updates-over-a-persistent-connection]]
