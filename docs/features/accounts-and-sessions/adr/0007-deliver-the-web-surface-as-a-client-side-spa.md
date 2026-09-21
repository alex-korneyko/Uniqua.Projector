---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-21"
feature_size: "M"
ticket: "n/a — roadmap step 2, docs/features/accounts-and-sessions"
---

# 0007 — Deliver the web surface as a client-side SPA

- **Status:** Accepted
- **Date:** 2026-09-21
- **Deciders:** Alex Korneiko (owner), Claude (facilitator)

## Context

Declaring a `web-frontend` surface (ADR 0006) forces a follow-on choice about how that surface is delivered: pages rendered on the server, a single page that loads JavaScript and then calls JSON endpoints, or a hybrid where only the authentication forms are server-rendered. For an authentication feature the choice is not cosmetic — it decides whether cross-site request-forgery protection is inherited or configured, and what an arriving visitor sees before JavaScript has loaded.

## Decision drivers

- `architecture-map.md` §Stack: React 19 + Vite + TanStack Query, with the API serving the build output — the foundation already describes a SPA.
- `architecture-map.md` §Conventions: exactly one **styling** mechanism — a second one (CSS modules, styled-components, inline style objects) is «a review finding, not a preference». The map states no rendering rule.
- ADR 0004: the board carries live updates and drag-and-drop, so substantial client-side state exists regardless of what this feature does.
- ADR 0003 (negative consequence): cookie authentication needs correct same-site attributes and cross-site request protection on state-changing endpoints.

## Considered options

1. **Client-side SPA** — registration and sign-in are React screens posting JSON to the same endpoints as the rest of the product.
2. **Hybrid: server-rendered authentication forms, SPA after sign-in** — the two forms are ordinary server-rendered pages; the browser enters the SPA once signed in.

## Decision outcome

**Chosen:** Option 1. The decisive driver is §Stack: the foundation already provisions React 19 + Vite + TanStack Query with the API serving the build output, and ADR 0004 puts substantial client state on the board screen regardless — so the SPA exists either way, and option 2 adds a second delivery path beside it rather than replacing it. Option 2's real advantages are genuine: inherited forgery protection and a first screen that works without JavaScript. They are outweighed by keeping one way to build a screen and one way to render a validation error. Note the map's «review finding, not a preference» rule is about a second **styling** mechanism specifically; applying the same reasoning to rendering is this decision's own judgement, not a quotation. The forgery protection option 2 would have given for free becomes an explicit §8 row instead, which suits a project whose wedge is that its access-control rules are written down.

## Consequences

**Positive**
- One rendering approach, one component library, one error-presentation pattern across the whole product — which is what the fifteen-minute read is being optimised for.
- The authentication screens reuse the vendored shadcn/ui primitives and Tailwind tokens already inventoried in `architecture-map.md` §Frontend, rather than introducing their own.

**Negative**
- Cross-site request-forgery protection must be configured deliberately on state-changing endpoints; getting it wrong is the failure ADR 0003 already flagged as easy to get subtly wrong. §8 carries the row and §10 carries the verification.
- A visitor sees an empty page until the bundle loads. For a reader who gives the link one minute, first-render time is worth watching; it is recorded as a §11 risk rather than an NFR, because the spec sets no number for it.

**Neutral**
- Moving an individual screen to server rendering later is possible but would reintroduce the second-mechanism problem, so it is unlikely to be worth it.


## Links

- Spec: [[../spec.md]]
- SAD: [[../sad.md]] §4
