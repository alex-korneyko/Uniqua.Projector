---
status: Accepted
owner: "Alex Korneiko"
reviewers: ["Tech Lead"]
updated_at: "2026-09-24"
feature_size: "M"
ticket: "roadmap step 3 — boards-columns-cards"
---

# 0018 — Open a board with card summaries and load a card's text on demand

- **Status:** Accepted
- **Date:** 2026-09-24
- **Deciders:** Alex Korneiko, with Claude during `/sdd:design boards-columns-cards`

## Context

A board can hold 20 columns and 1,000 cards, and a card description can be up to 10,000 characters (spec §6 content ceilings), so a board sent whole is on the order of 10 MB at the ceiling. The board screen (`ux-flows.md` SCR-04) shows card titles only; the description appears only in card detail (SCR-05). This record fixes the shape of the feature's main read, which both the API contract and the client's cached board are built on.

## Decision drivers

- Spec §6 NFR — latency p95 ≤ 300 ms for opening a board holding 20 columns and 1,000 cards.
- AC-23 — a member editing a card works from the card as they opened it; the card they open should be current.
- ADR 0016 — the client must hold the version counters it last saw (the board's column layout, each column's name, each card's content).
- `architecture-map.md` §Frontend — TanStack Query owns server state; a change or a push event patches the cached board rather than keeping a second copy.

## Considered options

1. **Summaries, text on demand** — opening a board returns the board, its columns and each card's summary (identity, column, position, title, content version) without descriptions; opening a card fetches its full text.
2. **Everything in one read** — opening a board returns every card with its description.
3. **Columns first, cards per column** — opening a board returns the columns; each column then fetches its own card summaries.

## Decision outcome

**Chosen:** Option 1. A full board stays around 150 KB, comfortably inside the 300 ms budget, and card detail always shows current text, which is what AC-23's comparison is made against. Option 2 puts the latency target at risk at the ceiling and re-downloads every description whenever the board refreshes; option 3 turns one screen into 21 requests and the latency target into their sum.

## Consequences

**Positive**
- The board read is bounded by titles (≤ 150 characters each), not descriptions; the p95 target has headroom.
- Opening a card is a small, separately cacheable read, and its `ContentVersion` is the one the member's edit is checked against.

**Negative**
- One extra request each time a card is opened.
- The card's summary (in the board cache) and its detail (in the card cache) must agree; every accepted card change patches both from the server's answer, and every stale refusal patches both from the current state it carries.

**Neutral**
- The step 8 push channel patches the same two cache entries; the event contract ADR 0004 asks to keep narrow fits this split.

## Links

- Spec: [[../spec.md]] AC-12, AC-13, AC-23, §6
- SAD: [[../sad.md]] §5
- UX flows: [[../ux-flows.md]] SCR-04, SCR-05
- Related ADR: [[0016-detect-stale-changes-with-per-concern-version-counters-in-the-domain]]
