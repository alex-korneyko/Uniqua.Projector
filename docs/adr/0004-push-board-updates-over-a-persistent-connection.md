---
status: Accepted
owner: "Alex Korneiko"
reviewers: []
updated_at: "2026-09-20"
feature_size: ""
ticket: "n/a — foundational decision from the survey session"
---

# 0004 — Push board updates to members over a persistent SignalR connection

- **Status:** Accepted
- **Date:** 2026-09-20
- **Deciders:** Alex Korneiko (owner), Claude (facilitator)

## Context

The success criterion ends with a second person being invited to a board, so two browsers will be open on
the same board during any demonstration. Whether the second browser learns about the first one's change on
its own, or only after a reload, changes how the client talks to the server, and retrofitting it later is
more expensive than building it in. The owner accepted a longer schedule specifically to keep this in scope.

## Decision drivers

- A Kanban clone is a saturated category; the live channel plus the written reasoning is where this project's
  differentiation is expected to come from (`idea-brief.md` §2, §6, §7).
- Two members on one board is part of the stated success criterion, not an extra (`idea-brief.md` §2).
- The channel must not become a way around access control (`idea-brief.md` §6).

## Considered options

1. **No live updates** — the second browser sees the change after a reload.
2. **Periodic polling** — the client asks the server for changes every few seconds.
3. **A persistent server-push connection (SignalR)** — the server sends changes as they happen.
4. **Defer it until after the first deploy** — ship without, add later.

## Decision outcome

**Chosen:** Option 3, over the facilitator's recommendation of option 1 and with the schedule extended from
four weeks to eight–ten to pay for it (see `idea-brief.md` §4). SignalR is already part of the chosen server
framework, so this adds a capability rather than a dependency. Connections are grouped per board and a
connection is admitted to a board's group only after the same membership check the HTTP endpoints use.

## Consequences

**Positive**
- The one feature that makes this repository distinguishable from other clones of the same product, and a
  real subject to discuss rather than a feature list.
- Reuses the existing authentication and the existing membership check; no second authorization model.

**Negative**
- A long tail beyond sending messages: reconnection after a dropped connection, and defined behaviour on a
  host that suspends an idle application. Hosting that keeps a long-lived connection alive is now a
  requirement, which further narrows the already-constrained week-2 hosting decision.
- Concurrent edits become visible rather than hidden, which makes an explicit conflict rule unavoidable
  sooner. The rule itself is still open (`idea-brief.md` §8).

**Neutral**
- The agreed fallback if the schedule slips is to narrow the channel to a single event type — the card move —
  and let everything else arrive on reload. Recording that now is what makes the fallback cheap; it requires
  the event contract to be designed so that removing event types does not reshape the client.

## Links

- Idea brief: [[../idea-brief.md]] §2, §4, §6, §7, §8
- Architecture map: [[../architecture-map.md]] §C4, §Constraints
- Related ADR: [[0003-authenticate-with-identity-and-an-httponly-cookie]]
