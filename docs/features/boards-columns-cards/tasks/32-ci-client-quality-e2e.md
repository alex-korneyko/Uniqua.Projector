---
id: T32
title: "Run the client component tests, the Quality suites and the e2e flows in CI"
layer: "ci"
deps: ["T26", "T27", "T31"]
acs: []
owner: "Alex Korneiko"
estimate: "S"
status: "todo"
origin: "review 2026-09-26 — findings B4"
---

# T32 — Run the client component tests, the Quality suites and the e2e flows in CI

Follow-up from [review-2026-09-26.md](../_review/review-2026-09-26.md), findings B4. See the finding rows there for the cited lines.

## Definition of Done

- [ ] CI runs `npm test`, a named Quality job for the race and column-order suites, and the e2e job; the workflow YAML is valid.
