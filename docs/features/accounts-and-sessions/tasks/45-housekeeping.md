---
id: T45
title: "Remove the tracked crash dump and bring every task file status in line with the tracker"
layer: "config"
deps: ["T35", "T36", "T37", "T38", "T39", "T40", "T41", "T42", "T43", "T44"]
acs: []
files_hint:
  - "grep.exe.stackdump"
  - ".gitignore"
  - "docs/features/accounts-and-sessions/tasks/"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 (re-review) — N-14"
status: "done"
---

# T45 — Remove the tracked crash dump and bring every task file status in line with the tracker

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent re-review — N-14 in [`_review/review-2026-09-22-2.md`](../_review/review-2026-09-22-2.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** T35, T36, T37, T38, T39, T40, T41, T42, T43, T44 (shared files, see `files_hint`).

## Acceptance criteria

- none directly: a quality finding (stage 2). The Definition of Done below is the bar.

## Definition of Done

- [ ] grep.exe.stackdump is removed from the index and *.stackdump is git-ignored; every docs/features/accounts-and-sessions/tasks/*.md frontmatter status matches tracker.md.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
