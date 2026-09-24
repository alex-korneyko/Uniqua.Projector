---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-24"
feature_size: "M"
ticket: "roadmap step 3 — boards-columns-cards"
---

# 0014 — Enforce membership by loading the board scoped to the caller

- **Status:** Accepted
- **Date:** 2026-09-24
- **Deciders:** Alex Korneiko, with Claude during `/sdd:design boards-columns-cards`

## Context

This feature introduces the product's first authorisation boundary. Spec §6.1 fixes the order of checks — session, per-account change limit, membership, then everything else — and requires that every column and card a request names belongs to the board whose membership was checked. A non-member must receive exactly the refusal a nonexistent board gets, including for a change that is itself invalid or stale (AC-25), and a member naming another board's column or card must be refused as if it did not exist (AC-26). The adversarial pass named cross-board substitution the sharpest failure, and showed that the stale-change refusal — which returns current state — would leak a whole board if it were ever answered before membership.

*Not considered:* ASP.NET Core resource-based authorization handlers — they would move the membership and owner rules into framework classes in Api, which `CLAUDE.md` rules out (domain rules live in Domain).

## Decision drivers

- Quality goal 1 (SAD §1) — an indistinguishable membership boundary; spec §6 NFR "Indistinguishable refusal": 100% of read and change kinds, 0 differences.
- `CLAUDE.md` — domain rules live in Domain; a use case orchestrates, it does not decide what is legal.
- AC-22 — only the board owner may rename or delete the board.
- ADR 0004 — the live-update hub (step 8) admits a connection only after the same membership check the HTTP endpoints use.

## Considered options

1. **A member-scoped load in the use case** — every use case begins with a port call that returns the board only if the caller is a member; absent and not-a-member collapse into one application error; columns and cards are found through the loaded board; the owner rule is a Board method.
2. **An endpoint filter on the boards route group** — an ASP.NET Core endpoint filter checks membership before each handler runs.

## Decision outcome

**Chosen:** Option 1. The check runs where the board is loaded, so there is no way to reach a board — or anything on it — without passing it, and the item-belongs-to-board rule falls out of looking items up through the board rather than being a second check someone can forget. The owner rule stays a domain method, and the same use-case entry point serves the hub at step 8. Option 2 splits the rule across Api and Domain and cannot be reused by the hub.

## Consequences

**Positive**
- One error, `BoardNotAvailable`, for "absent", "not a member", "deleted" and "names a column or card not on this board"; `ProblemDetailsSetup` maps it to one refusal, so the NFR comparison test has exactly one shape to compare.
- The §6.1 order is visible in every use case: the per-account limit (Api, before the use case), the member-scoped load, then validation, the stale check and the invariants.

**Negative**
- Request bodies must be bound leniently — values taken as they arrive and validated by the domain after the membership check — so the framework never rejects an invalid value before membership. A body that is not JSON at all is refused before membership, identically for every board, which reveals nothing; the `api` stage fixes the exact binding.
- Every use case must start with the scoped load; a use case that skips it is a defect that only review and the NFR comparison test catch.

**Neutral**
- Integration tests prove the boundary by inserting a `Member` record (ADR 0013) and comparing non-member and nonexistent-board responses field for field.

## Links

- Spec: [[../spec.md]] §6.1, AC-22, AC-25, AC-26, AC-27
- SAD: [[../sad.md]] §4, §8
- Related ADR: [[0013-grant-board-access-through-one-level-membership-records-with-an-owner-role]], 0004 (`docs/adr/`)
