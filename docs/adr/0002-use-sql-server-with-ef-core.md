---
status: Accepted
owner: "Alex Korneiko"
reviewers: []
updated_at: "2026-09-20"
feature_size: ""
ticket: "n/a — foundational decision from the survey session"
---

# 0002 — Store data in SQL Server, access it through EF Core, and migrate with EF Core migrations

- **Status:** Accepted
- **Date:** 2026-09-20
- **Deciders:** Alex Korneiko (owner), Claude (facilitator)

## Context

The application holds accounts, boards, columns, cards, checklist items, memberships and invitation
tokens — relational data with a small, well-understood shape. The store, the access mechanism and the
migration tool cannot be chosen independently, because the migration tool follows from the access
mechanism. The choice is due now because the first production deploy is fixed at week 2 and the store
must be hosted by then.

## Decision drivers

- The success criterion is a public link a stranger can use unattended, which requires hosted, durable
  storage well before the project is finished (`idea-brief.md` §2).
- The owner works with this stack professionally, so skill transfer between the day job and the project
  is a stated benefit of the learning project (`idea-brief.md` §1).
- The schedule leaves no room for hand-writing routine persistence code (`idea-brief.md` §4).

## Considered options

1. **PostgreSQL + EF Core + EF Core migrations** — the cheapest to host; broad free tiers.
2. **PostgreSQL + Dapper + a standalone SQL migration tool** — hand-written SQL throughout.
3. **SQL Server + EF Core + EF Core migrations** — the Microsoft-native pairing.
4. **PostgreSQL + EF Core for writes, Dapper for the board read** — a split read/write path.

## Decision outcome

**Chosen:** Option 3. The owner chose SQL Server over the recommended PostgreSQL after the hosting cost
was raised explicitly, on the grounds of stack familiarity and transfer to professional work. EF Core is
kept as the access mechanism for schedule reasons — hand-written SQL was judged too expensive for an
evenings project that has already committed to sacrificing tests under pressure. Migrations are generated
from the model, which keeps the schema and the code in one place.

## Consequences

**Positive**
- Model, access and migrations are one toolchain with one command each — the shortest path to a deployed,
  migrating application.
- The skill is directly transferable to the owner's professional work.

**Negative**
- Hosting is materially harder and generally not free, which is in direct tension with a success criterion
  that requires the link to stay alive unattended for months. This is recorded as the top constraint in
  the architecture map and must be resolved in week 1.
- The SQL Server test container is heavy and slow to start, so the agreed integration test floor pays a
  cost on every local run and in CI.
- Generated SQL is hidden behind the mapping layer; a slow board query will have to be diagnosed through
  logging rather than read directly.

**Neutral**
- Moving to PostgreSQL later is mostly a provider swap plus regenerated migrations, because no
  vendor-specific feature is planned. It is not free, but it is not a rewrite either.

## Links

- Idea brief: [[../idea-brief.md]] §2, §4, §8
- Architecture map: [[../architecture-map.md]] §Datastores, §Constraints
- Related ADR: [[0001-split-the-backend-into-four-projects]]
