---
id: T60
title: "Anchor the .gitignore build-output patterns so they cannot swallow source or docs folders"
layer: "config"
deps: []
acs: []
files_hint:
  - ".gitignore"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (third re-review) — T-05"
status: "done"
---

# T60 — Anchor the .gitignore build-output patterns so they cannot swallow source or docs folders

## Place in the sequence

- **Origin:** a verdict from the independent third re-review — T-05 in [`_review/review-2026-09-23-2.md`](../_review/review-2026-09-23-2.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- — (no AC; a quality finding)

## Definition of Done

- [ ] [Dd]ebug/, [Rr]elease/, [Ll]og/, [Ll]ogs/, artifacts/ and **/[Pp]ackages/* no longer match at any depth: either anchored to where build output actually lands, or dropped where bin/ and obj/ already cover .NET output; the *.sln.DotSettings comment is corrected for a .slnx repo; checked directly: git check-ignore returns nothing for src/Uniqua.Projector.Application/Logs/A.cs, src/Uniqua.Projector.Web/src/debug/p.tsx, src/Uniqua.Projector.Web/src/features/packages/i.ts and docs/features/x/artifacts/a.md, still ignores src/*/bin/, src/*/obj/, node_modules/ and the Api's wwwroot build output if it was ignored before, and git ls-files -ci --exclude-standard stays empty.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
