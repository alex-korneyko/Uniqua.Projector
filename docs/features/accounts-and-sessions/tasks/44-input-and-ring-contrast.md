---
id: T44
title: "Darken the input border and focus-ring tokens to at least 3:1 against the background"
layer: "ui"
deps: []
acs: []
files_hint:
  - "src/Uniqua.Projector.Web/src/index.css"
  - "src/Uniqua.Projector.Web/src/test/themeTokens.test.ts"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 (re-review) — N-13"
status: "todo"
---

# T44 — Darken the input border and focus-ring tokens to at least 3:1 against the background

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent re-review — N-13 in [`_review/review-2026-09-22-2.md`](../_review/review-2026-09-22-2.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (shared files, see `files_hint`).

## Acceptance criteria

- none directly: a quality finding (stage 2). The Definition of Done below is the bar.

## Definition of Done

- [ ] --color-input and --color-ring in src/index.css reach a contrast ratio of at least 3:1 against --color-background (WCAG 1.4.11), in the light and, where defined, the dark theme; src/test/themeTokens.test.ts computes the ratio from the declared oklch values and fails below 3:1.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
