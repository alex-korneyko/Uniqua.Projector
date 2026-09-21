---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-21"
feature_size: "M"
ticket: "n/a — roadmap step 2, docs/features/accounts-and-sessions"
---

# 0006 — Build this feature as a backend service and a web front-end

- **Status:** Accepted
- **Date:** 2026-09-21
- **Deciders:** Alex Korneiko (owner), Claude (facilitator)

## Context

The spec states the feature product-level and never names a surface. Its first goal is that a stranger reaches a signed-in state from the public link with no intervention from the owner, and AC-01/AC-02/AC-04/AC-05 all describe someone submitting and being answered by a form. What the feature actually ships — which C4 containers it introduces — has to be decided before §5 can be drawn, because every downstream stage gates its own output on that declaration.

## Decision drivers

- Spec §2: «a stranger reaches a signed-in state from the public link … in one session» — unreachable without screens.
- Spec §5: AC-01, AC-02, AC-02b, AC-03, AC-04, AC-05, AC-11b all describe a form refusing or accepting input in plain language.
- ADR 0003: the client and the API must share one origin, so the API serves the built client — the two surfaces ship as one deployable.
- `architecture-map.md`: the foundation already provisions a React client project (`src/Uniqua.Projector.Web/`).

## Considered options

1. **Backend service + web front-end** — this feature builds the endpoints, the schema *and* the registration/sign-in screens.
2. **Backend service only** — this feature builds endpoints and schema; the screens are deferred to a separate later feature.

## Decision outcome

**Chosen:** Option 1. Option 2 cannot satisfy the feature's own acceptance criteria: AC-01 and AC-04 are written as observable form behaviour, so a backend-only build would close the feature with its primary goal unmet and `review` would refuse it. The screens are not additional scope — they are where the spec's goal is observed.

## Consequences

**Positive**
- The feature can be demonstrated end to end on the public link, which is what the week-2 deployment exists to show.
- `screens` runs after `api` and produces a per-state manifest, so the error paths (AC-02, AC-02b, AC-03, AC-05, AC-11b) get designed rather than improvised.

**Negative**
- Two surfaces make the feature larger than a single-surface M: `tasks` adds a `ui` layer and `plan-tests` adds the component and e2e-through-UI tiers. The M sizing is tight as a result.
- `ux-flows` was skipped, so the screen inventory that would normally be evidence for this choice does not exist; `screens` will derive states from the acceptance criteria and the contract's error responses instead.

**Neutral**
- Both surfaces deploy as one unit because of the shared origin, so the surface split adds no deployment complexity.


## Links

- Spec: [[../spec.md]]
- SAD: [[../sad.md]] §4
