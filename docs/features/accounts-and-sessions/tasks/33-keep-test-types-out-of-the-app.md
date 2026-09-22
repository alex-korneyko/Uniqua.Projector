---
id: T33
title: "Type-check tests in their own program and drop vitest globals"
layer: "config"
deps: []
acs: []
files_hint:
  - "src/Uniqua.Projector.Web/tsconfig.app.json"
  - "src/Uniqua.Projector.Web/tsconfig.test.json"
  - "src/Uniqua.Projector.Web/tsconfig.json"
  - "src/Uniqua.Projector.Web/vite.config.ts"
  - "src/Uniqua.Projector.Web/src/test/setup.ts"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-28"
status: "done"
---

# T33 — Type-check tests in their own program and drop vitest globals

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-28 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (a shared file, see `files_hint`).

## Acceptance criteria

- none directly: a quality finding (stage 2). The Definition of Done below is the bar.

## Definition of Done

- [ ] tsconfig.app.json excludes __tests__ and src/test and has no test types; tsconfig.test.json carries them; vitest runs without globals: true; tsc -b, lint and the web suite stay green.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
