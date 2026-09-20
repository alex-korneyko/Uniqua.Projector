---
status: Accepted
owner: "Alex Korneiko"
reviewers: []
updated_at: "2026-09-20"
feature_size: ""
ticket: "n/a — foundational decision from the survey session"
---

# 0005 — Build the client with Vite and Tailwind, and vendor shadcn/ui components into the repository

- **Status:** Accepted
- **Date:** 2026-09-20
- **Deciders:** Alex Korneiko (owner), Claude (facilitator)

## Context

The client needs a build tool, one styling mechanism, a source of ready-made interface pieces (buttons,
dialogs, menus) and a way to hold server data in the browser. These are one decision rather than four,
because mixing styling approaches in a single repository is immediately visible and reads as carelessness.
The choice also has to respect the single-origin constraint that cookie authentication imposes.

## Decision drivers

- The reviewing engineer reads the client as code, not only as a rendered page (`idea-brief.md` §3).
- The application has almost no forms; it has one complex screen with drag-and-drop, so control over layout
  matters more than a catalogue of form controls (`idea-brief.md` §1).
- The built client is served by the API to keep one origin (ADR 0003).
- Every dependency must be permissively licensed; no copyleft component may enter the project.

## Considered options

1. **Tailwind + shadcn/ui** — utility styling, component source copied into the repository and owned there.
2. **MUI** — a large installed component library with a theme.
3. **Tailwind with no component source** — every control written by hand.
4. **Next.js instead of plain React** — a full framework with its own server.

## Decision outcome

**Chosen:** Option 1, on the stack Vite + React 19 + TypeScript, with TanStack Query owning all server
state and dnd-kit for the card drag-and-drop. The board is a custom layout that a general component library
does not help with, while the card dialog and menus are exactly the accessible pieces worth not writing by
hand — which is the split this option gives. Option 4 was rejected because a second server next to the
existing one adds a runtime to host and invites the question of where the logic lives.

## Consequences

**Positive**
- Full control over the board screen, with accessible dialogs and menus obtained for free.
- The component code is in the repository and readable, which suits a reader who evaluates code.
- All of Vite, React, Tailwind, shadcn/ui, TanStack Query and dnd-kit are MIT-licensed; no copyleft exposure.

**Negative**
- More code in the repository, and upgrading a vendored component is a manual act with no single command.
- Long utility-class strings in markup are divisive and will read as noise to some reviewers.

**Neutral**
- Exactly one styling mechanism is allowed. A second one appearing later is a review finding rather than a
  matter of taste, and that rule is recorded in the architecture map's conventions.

## Links

- Idea brief: [[../idea-brief.md]] §1, §3
- Architecture map: [[../architecture-map.md]] §Frontend / UI foundation
- Related ADR: [[0003-authenticate-with-identity-and-an-httponly-cookie]]
