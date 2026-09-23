---
id: T68
title: "Anchor or drop the remaining unanchored .gitignore build-output patterns"
layer: "config"
deps: []
acs: []
files_hint:
  - ".gitignore"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (fourth re-review) — V-07"
status: "todo"
---

# T68 — Anchor or drop the remaining unanchored .gitignore build-output patterns

## Place in the sequence

- **Origin:** a verdict from the independent fourth re-review — V-07 in [`_review/review-2026-09-23-3.md`](../_review/review-2026-09-23-3.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- None — this task proves or tidies existing behaviour; see the finding in the review record.

## Definition of Done

- [ ] publish/, x64/, x86/, [Aa][Rr][Mm]/ and [Aa][Rr][Mm]64/ no longer match at any depth: anchored to where build output actually lands, or dropped where bin/ and obj/ already cover it; checked directly: git check-ignore reports nothing for src/Uniqua.Projector.Web/src/features/publish/x.ts, src/Uniqua.Projector.Web/src/x64/x.ts and docs/features/x/arm/x.md, while bin/, obj/, node_modules, dist, the API's wwwroot build output and TestResults stay ignored, and git ls-files -ci --exclude-standard lists nothing.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
