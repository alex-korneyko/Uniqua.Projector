---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-21"
feature_size: "M"
ticket: "n/a — roadmap step 2, docs/features/accounts-and-sessions"
---

# 0009 — Keep the data-protection key ring in the database

- **Status:** Accepted
- **Date:** 2026-09-21
- **Deciders:** Alex Korneiko (owner), Claude (facilitator)

## Context

Even with server-side session records (ADR 0008) the cookie carrying the session reference must be signed and encrypted, or anyone could substitute another account's session id. ASP.NET Core Data Protection does this, and by default keeps its key ring in a folder belonging to the process — which disappears when the container is replaced. Spec §6 and KPI 3 both commit to 100% of unexpired sessions surviving a redeploy, so the default would silently break a stated promise on the first deployment.

## Decision drivers

- Spec §6, «Session survival across a redeploy»: 100% of unexpired sessions survive.
- Spec §7, KPI 3: checked after every deployment from week 2 — so the first check happens almost immediately.
- Spec AC-06 and AC-07: a 14-day promise that a redeploy would otherwise quietly shorten to «until the next deploy».
- The instance is self-hosted on the owner's own host, where the database from ADR 0002 is deployed alongside the application.
- Deployment is manual and infrequent; anything that must be remembered by hand at deploy time will eventually be forgotten.

## Considered options

1. **A table in the same database** — Data Protection persists the key ring through EF Core into the store that already exists.
2. **Files on a mounted host volume** — the key ring lives in a directory on the host, mounted into the container.
3. **A table in the database, encrypted at rest with a certificate** — as option 1, but the keys themselves are protected by a certificate held outside the database.

## Decision outcome

**Chosen:** Option 1. Option 2 separates the keys from the data they protect, which is the cleaner arrangement in principle, but it adds a second artefact to back up and a mount to remember — and a forgotten mount fails silently, restoring exactly the behaviour this decision exists to prevent. Option 3 removes option 1's main weakness but introduces a third piece of secret material into a project that has no secret manager and a fixed week-2 deadline. On a single self-hosted host, whoever can read the key volume can almost always read the database files next to it, so option 1's theoretical weakness is largely notional here.

## Consequences

**Positive**
- Sessions survive a redeploy, a container replacement and a move to another virtual machine, with nothing to configure at deploy time.
- The existing database backup covers the key ring; there is no second thing that can be forgotten.

**Negative**
- The keys that protect sessions live in the same store as the accounts they protect, so one database compromise yields both. Recorded in spec §6.1 as an explicit condition rather than left unsaid, and in §11 as accepted debt.
- Key-ring rows are operational state inside an otherwise domain-shaped schema; `data-model` keeps them clearly separated from the domain tables.

**Neutral**
- Moving to option 3 later is additive — it changes how the ring is protected, not where it lives — and costs one deployment during which sessions end.


## Links

- Spec: [[../spec.md]]
- SAD: [[../sad.md]] §4
