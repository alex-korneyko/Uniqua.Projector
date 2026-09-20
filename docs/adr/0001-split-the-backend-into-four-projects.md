---
status: Accepted
owner: "Alex Korneiko"
reviewers: []
updated_at: "2026-09-20"
feature_size: ""
ticket: "n/a — foundational decision from the survey session"
---

# 0001 — Split the backend into four projects: Api, Application, Domain, Infrastructure

- **Status:** Accepted
- **Date:** 2026-09-20
- **Deciders:** Alex Korneiko (owner), Claude (facilitator)

## Context

The repository is empty and the server side has to be laid out before anything is written. The project
is a portfolio artifact whose primary reader is a technical interviewer who will open the repository and
look at layer boundaries first. The layout has to be chosen now because every later decision is placed
inside it and changing it afterwards is a move of all the code.

## Decision drivers

- The primary user is a reviewing engineer who inspects structure before behaviour (`idea-brief.md` §3).
- The owner has declared that a schedule slip is absorbed by tests and finish rather than by features
  (`idea-brief.md` §5, §6) — so boundaries that survive fatigue must not depend on discipline alone.
- The schedule is 8–10 weeks of evenings for one developer (`idea-brief.md` §4), which rules out layouts
  whose overhead scales with the number of moving parts.

## Considered options

1. **Four projects — Api / Application / Domain / Infrastructure** — separate assemblies in one solution,
   boundaries enforced by project references.
2. **Vertical slices** — one project, one folder per use case containing endpoint, logic and query together.
3. **One project with layer folders** — the same layers as folders inside a single assembly.
4. **Modular monolith** — a module per domain area, each with its own internal layers and explicit contracts.

## Decision outcome

**Chosen:** Option 1. It is the only option where the boundary is checked by the compiler rather than by
the developer's discipline: code that reaches from Domain into persistence does not build. Given that the
owner has explicitly named quality as the thing that gets sacrificed under schedule pressure, a boundary
that holds without attention is worth more here than the flexibility of the alternatives. Option 4 was
rejected as oversized for three entities and one developer; option 3 was rejected because its boundaries
exist only on paper.

## Consequences

**Positive**
- The dependency rule is mechanically enforced and demonstrable — the strongest single claim this
  repository can make to its intended reader.
- Each layer is unit-testable in isolation, which the agreed test floor relies on for Domain rules.

**Negative**
- A simple feature touches three or four projects, which is friction on every change.
- Solution and reference management is extra setup before the first line of behaviour is written.

**Neutral**
- If Domain ends up holding data classes without behaviour, the split becomes expensive decoration. This
  is guarded by an explicit convention in the architecture map ("domain rules live in Domain") rather than
  by the structure itself, and it is the main way this decision can quietly fail.

## Links

- Idea brief: [[../idea-brief.md]] §3, §5, §6
- Architecture map: [[../architecture-map.md]] §Module inventory, §Conventions
- Related ADR: [[0002-use-sql-server-with-ef-core]]
