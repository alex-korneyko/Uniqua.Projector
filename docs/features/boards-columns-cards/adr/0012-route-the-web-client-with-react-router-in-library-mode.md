---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead"]
updated_at: "2026-09-24"
feature_size: "M"
ticket: "roadmap step 3 — boards-columns-cards"
---

# 0012 — Route the web client with React Router in library mode

- **Status:** Accepted
- **Date:** 2026-09-24
- **Deciders:** Alex Korneiko, with Claude during `/sdd:design boards-columns-cards`

## Context

Until this feature the client has no router: `AccountShell` swaps between the visitor screens and a signed-in placeholder. `ux-flows.md` fixes two addressable places — the board list and one board — because US-10 assumes a link to a board exists and can be shared or bookmarked, and AC-27 requires that a visitor who signs in from such a link is returned to that address. The return address must be limited to addresses inside the application so it cannot become an open redirect. The SPA itself is inherited from ADR 0007; this record decides only how the SPA gets addresses.

## Decision drivers

- US-10 / AC-27 — a board has an address of its own, and sign-in returns to it (spec §5, `ux-flows.md` §Platform decisions).
- Quality goal 1 (SAD §1) — the return address must not become an open redirect, and a bookmarked board must answer exactly as the membership rule says.
- The reviewing engineer of spec §1 reads the repository for fifteen minutes; a router they recognise costs no reading time.
- Licensing — permissive only (`CLAUDE.md`).
- TanStack Query already owns all server state (`architecture-map.md` §Frontend) — the router must not become a second data cache.

## Considered options

1. **React Router, library mode** — `react-router` (MIT) as a client router only: routes for the board list, `/boards/:boardId` and sign-in; data loading stays in TanStack Query.
2. **TanStack Router** — `@tanstack/react-router` (MIT): type-checked route and search parameters, from the same family as TanStack Query.
3. **Hand-rolled on the History API** — no dependency; a small hook matching `window.location` and pushing history entries.

## Decision outcome

**Chosen:** Option 1. It is the router most readers already know, so the routes and the return-address check read at a glance, and library mode keeps it to routing — its loader and action features are deliberately left unused so TanStack Query stays the only server-state owner. Option 2's type safety is real but buys little for three routes and brings either a code-generation step or verbose code-based routes; option 3 turns back/forward, links and not-found handling into code this project would have to own and test for every screen later features add.

## Consequences

**Positive**
- Board addresses are bookmarkable and shareable; sign-in can return to `returnTo` with one guard.
- Later screens (card detail as an overlay route, invitations at step 7) slot into the same route table.

**Negative**
- Roughly 20 KB of added JavaScript, against a first-render time already watched as a risk (accounts-and-sessions SAD §11).
- The router's data APIs overlap with TanStack Query; a contributor could start using loaders and create a second cache. Guarded by a SAD §8 convention row, not by tooling.

**Neutral**
- The `returnTo` guard — accept only a same-origin path beginning with a single `/`, never `//` or a scheme — is ours to write and test regardless of router.
- Swapping to option 2 later is a mechanical rewrite of the route table and links, not of screens.

## Links

- Spec: [[../spec.md]] US-10, AC-27
- SAD: [[../sad.md]] §4
- UX flows: [[../ux-flows.md]] §Platform decisions, flow US-10
- Related ADR: 0007 (client-side SPA, `docs/features/accounts-and-sessions/adr/`)
