---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-24"
feature_size: "M"
ticket: "roadmap step 3 — boards-columns-cards; roadmap D5"
---

# 0013 — Grant board access through one-level membership records with an owner role

- **Status:** Accepted
- **Date:** 2026-09-24
- **Deciders:** Alex Korneiko, with Claude during `/sdd:design boards-columns-cards`

## Context

Roadmap decision D5 fixed that membership is one level — an account belongs to a board directly, and a project, if one ever exists, grants no access of its own — and asked that this be recorded as an ADR during this design (spec §8, second open question). The glossary already states the rule (`board`, `board member`, `board owner` in `CONTEXT.md`). What remained open is how access is recorded while every board still has exactly one member: invitations, the only way to add a second member, arrive at roadmap step 7, yet AC-21 and AC-22 must be proved now against a member who is not the owner.

## Decision drivers

- Spec §6.1 abuse case *owner-only check posing as a membership check* — while every board has one member, a check comparing the requester to the owner passes every test.
- AC-21 / AC-22 — the member-level and owner-level capabilities are each exercised against a board member who is not the owner, created by integration-test setup only (spec §3).
- Spec §3 non-goal — projects grouping several boards would reach into every access-control check.
- Roadmap step 7 — invitations turn a stranger into a board member; step 8 — the hub admits a connection after the same membership check (ADR 0004).

## Considered options

1. **Membership records with a role** — one record per (board, account) carrying `Owner` or `Member`; creating a board writes the creator's `Owner` record.
2. **An owner column now, a members table at step 7** — `OwnerId` on the board; "member" means "owner" until invitations exist.
3. **An owner column plus a members table** — `OwnerId` for the owner rights, and a members table listing everyone with access, owner included.

## Decision outcome

**Chosen:** Option 1. The membership check is one query whoever is asking, so an integration test that inserts a `Member` record exercises exactly the path production uses, and invitations only add records. Option 2 is the spec §6.1 abuse case itself: without a members table no test can create a non-owner member, so AC-21/22 cannot be proved as the spec requires. Option 3 creates two sources of truth that must agree — an owner missing from the members table would lose access to their own board.

## Consequences

**Positive**
- The membership check and the owner check read the same record; the owner rule is a domain method over it (ADR 0014).
- Step 7 adds members without changing the membership shape; step 8's hub reuses the same check.

**Negative**
- One extra table and join now, for boards that all have exactly one member until step 7.
- "Exactly one `Owner` per board" becomes an invariant the domain must hold — board creation writes it, and nothing in this feature changes it (transferring ownership is out of scope, spec §3).

**Neutral**
- No operation that adds a member ships in this feature, hidden or otherwise (spec §3); the `Member` role is reachable only from integration-test setup until step 7.
- A future project grouping would sit above boards and grant nothing, per `CONTEXT.md` `board`.

## Links

- Spec: [[../spec.md]] §3, §6.1, AC-21, AC-22, §8 (second open question)
- SAD: [[../sad.md]] §4
- Roadmap: `docs/roadmap.md` D5
- Related ADR: [[0014-enforce-membership-by-loading-the-board-scoped-to-the-caller]]
