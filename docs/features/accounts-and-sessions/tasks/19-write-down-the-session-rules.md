---
id: T19
title: "Write the four session rules into the repository where the fifteen-minute read finds them"
layer: "docs"
deps: ["T12", "T14"]
blocks: []
acs: []
files_hint:
  - "docs/session-rules.md"
  - "README.md"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "todo"
---

# T19 — Write the four session rules into the repository where the fifteen-minute read finds them

## Place in the sequence

- **Blocked by:** T12 — session recognition, T14 — the sign-in and sign-out endpoints · **Blocks:** — · **Wave:** 6. It comes last on purpose: each rule it states must point at the code that enforces it.
- **Lane:** own lane — the only task writing outside `src/`, `tests/` and the feature folder.

## Why (user story)

> **As an** account
> **I want** my session to survive closing the browser
> **So that** returning to the link days later does not cost me a sign-in
>
> — `spec.md §4, US-03, verbatim` · full text: [spec.md](../spec.md)

Every other task makes the rules true. This one makes them *findable* — which is the half of the committed approach that no acceptance criterion can express.

**No §5 acceptance criterion covers this task** — `acs` is deliberately empty. It discharges spec §2's second goal and §7's fourth KPI, both quoted below, and it is the one task whose absence would leave the feature's headline promise unkept while every test still passed.

## Inlined context

> The committed approach is to make every rule governing a session **both stated in the repository and checkable from outside it**, so that a reader can hold the written promise against the observed behaviour instead of taking either on trust. Four rules are fixed here — how a session is carried, how long it lives, what ends it, and what happens when someone guesses at a password — and each is written down where the fifteen-minute read will find it and is exercisable on the live link.
>
> — `spec.md §1, committed approach, verbatim` · full text: [spec.md](../spec.md)

> Competitive research found that no product in this category publishes any of these: one major collaboration product exposes session duration only as a buried administrator setting, and the leading board products document neither their session lifetime nor their post-registration behaviour, which is why a reader cannot learn them by clicking and must be handed them.
>
> — `spec.md §1, verbatim` · full text: [spec.md](../spec.md)

> The rules governing a session — how long it lives, what ends it, and what happens when someone guesses at a password — are each stated in the repository and observable from outside the application.
>
> — `spec.md §2, goal 2, verbatim` · full text: [spec.md](../spec.md)

> **A reviewer can answer "how is the session carried, and what ends it" from the repository alone** — baseline: not answerable, nothing written; target: ≤ 2 minutes. Measurement plan: ask the first reviewer to try, and time it. This is the committed approach's own success measure.
>
> — `spec.md §7, KPI 4, verbatim` · full text: [spec.md](../spec.md)

> **Hard rule:** this feature is the one the fifteen-minute read examines first. When the three quality goals conflict, security wins.
>
> — `sad.md §1, abridged` · full text: [sad.md](../sad.md)

**Do not restate the spec.** This document is for a reader who will never open `docs/features/`: short, plain, and pointing at the code and the test that make each claim true.

**Fallback:** insufficient or contradicted by the code → read the named file in full ([spec.md](../spec.md) · [sad.md](../sad.md)) and follow it. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface. The document links to `docs/features/accounts-and-sessions/contracts/openapi.yaml` as the contract of record rather than restating any endpoint.

## Acceptance criteria

None. This task carries no spec §5 acceptance criterion; the two commitments it discharges are spec §2's second goal and §7's KPI 4, quoted above. Its testable outcome is in the Definition of Done: each of the four rules is stated with a link to the code that enforces it and the test that proves it.

## Checklist

- [ ] `docs/session-rules.md` — four short sections, one per rule:
  - [ ] **How a session is carried** — an httpOnly, secure, same-site cookie holding an opaque reference to a server-side record; page scripts cannot read it, and no endpoint returns it (ADR 0003, ADR 0008).
  - [ ] **How long it lives** — 14 days from the last request, and never more than 90 days from when it was opened, whatever the activity.
  - [ ] **What ends it** — signing out ends exactly the session it travelled on, and the record is what refuses the next request, not the browser's forgetting; expiry ends it on the schedule above; a redeploy ends nothing (ADR 0009).
  - [ ] **What happens when someone guesses at a password** — a per-account delay growing with consecutive failures (≥ 2 s at the 6th, ≥ 30 s at the 10th), resetting on success or after 15 minutes idle, and **no lockout, ever** (ADR 0010).
- [ ] Each rule names the file that enforces it and the test that proves it, as clickable paths.
- [ ] Each rule names how to observe it from outside — the request to make, the cookie attribute to look at, the wait to measure.
- [ ] A short "what this does not do" list: no password recovery, no address verification, no third-party sign-in, no account removal (spec §3).
- [ ] Link it from `README.md` prominently enough to be found in the first minute, and link onward to the ADRs and the OpenAPI contract.
- [ ] A link check over every path and anchor the document cites.

## Edge cases

| Case | Behaviour |
|---|---|
| A number in the document disagrees with the code | The code is authoritative and the document is a defect — this is exactly the failure the feature's whole premise forbids, so fix it before the task closes |
| A reader wants the reasoning, not the rule | One link per rule to its ADR; the document itself stays short |
| A rule cannot be observed from outside yet (AC-09, redeploy survival) | Say so plainly, naming roadmap step 8 and step 4 — an honest "not yet verified" beats an unqualified promise |
| The document drifts after a later feature changes a rule | It names the enforcing file per rule, so a `grep` over those paths finds it during review |
| A reviewer reads only `README.md` | The four rules are named there in one line each, with the link for the detail |

## Definition of Done

- [ ] `docs/session-rules.md` exists, states all four rules, and each names its enforcing file and its proving test as working links.
- [ ] Each of the four rules names an observation a reader can make from outside the application.
- [ ] `README.md` links to it, and every link and anchor in the document resolves (checked, not assumed).
- [ ] The two commitments that cannot be verified yet are labelled with the roadmap step that verifies them (AC-09 → step 8; redeploy survival → step 4).
- [ ] A reader who has never opened `docs/features/` can answer "how is the session carried, and what ends it" from this document alone — verified by asking the first available reader and timing it against the ≤ 2 minute target (spec §7 KPI 4).
- [ ] every Hard Rule inlined above still holds
- [ ] lint clean (markdown link check)
