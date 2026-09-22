---
id: T32
title: "Define the theme tokens the vendored components read"
layer: "ui"
deps: []
acs: []
files_hint:
  - "src/Uniqua.Projector.Web/src/index.css"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-19"
status: "todo"
---

# T32 — Define the theme tokens the vendored components read

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-19 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (a shared file, see `files_hint`).

## Acceptance criteria

- none directly: a quality finding (stage 2). The Definition of Done below is the bar.

## Definition of Done

- [ ] --color-destructive, --color-card, --color-card-foreground, --color-input and --color-ring are defined in @theme; a production build emits CSS for text-destructive, bg-card, border-input and ring-ring.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
