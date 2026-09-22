---
id: T29
title: "Render a refusal's title as its statement and its detail as the reason"
layer: "ui"
deps: []
acs: ["AC-03", "AC-11b", "AC-01b"]
files_hint:
  - "src/Uniqua.Projector.Web/src/api/accounts.ts"
  - "src/Uniqua.Projector.Web/src/features/auth/accountRefusals.ts"
  - "src/Uniqua.Projector.Web/src/features/auth/__tests__/RegisterScreen.test.tsx"
  - "src/Uniqua.Projector.Web/src/api/__tests__/accounts.test.ts"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-10"
status: "done"
---

# T29 — Render a refusal's title as its statement and its detail as the reason

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-10 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (a shared file, see `files_hint`).

## Acceptance criteria

- AC-03 — verbatim in [spec.md §5](../spec.md)
- AC-11b — verbatim in [spec.md §5](../spec.md)
- AC-01b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] The taken-address, taken-name and rate-limit refusals read 'already registered', 'taken' and 'temporarily limited'; antiforgery_failed shows the server's detail; the retry sentence appears exactly once even when the detail already names the wait.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
