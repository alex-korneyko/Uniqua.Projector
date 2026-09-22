---
id: T39
title: "Prove an unavailable session store fails closed, and word the test-plan row as the UI behaves"
layer: "tests"
deps: []
acs: ["AC-10"]
files_hint:
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionRecognitionTests.cs"
  - "docs/features/accounts-and-sessions/test-plan.md"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 (re-review) — N-05"
status: "todo"
---

# T39 — Prove an unavailable session store fails closed, and word the test-plan row as the UI behaves

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent re-review — N-05 in [`_review/review-2026-09-22-2.md`](../_review/review-2026-09-22-2.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (shared files, see `files_hint`).

## Acceptance criteria

- AC-10 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] An integration test replaces ISessionReader with one that throws and shows /api/v1/me answers a non-2xx application/problem+json and never a 200 or the account view; the test-plan.md edge row for "store unavailable" is re-worded to "fails closed: the failure view with retry is shown, never the account view"; any production change needed stays inside the one ProblemDetails handler.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
