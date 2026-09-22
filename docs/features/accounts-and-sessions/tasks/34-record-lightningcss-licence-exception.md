---
id: T34
title: "Record lightningcss (MPL-2.0, build-time only) as a licence exception"
layer: "docs"
deps: []
acs: []
files_hint:
  - "docs/adr/0011-accept-lightningcss-as-a-build-time-licence-exception.md"
  - "docs/architecture-map.md"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-29"
status: "todo"
---

# T34 — Record lightningcss (MPL-2.0, build-time only) as a licence exception

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-29 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (a shared file, see `files_hint`).

## Acceptance criteria

- none directly: a quality finding (stage 2). The Definition of Done below is the bar.

## Definition of Done

- [ ] An Accepted ADR names lightningcss, its MPL-2.0 licence, the path that brings it in (@tailwindcss/vite), why it never ships in the bundle, and the condition that would revisit it; architecture-map links it.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
