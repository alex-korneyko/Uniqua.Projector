---
id: T40
title: "Record the unknown-address counter limits, the 14 days + 1 hour boundary, and close the ADR 0003 question"
layer: "docs"
deps: ["T35", "T39"]
acs: ["AC-05b", "AC-07", "AC-12"]
files_hint:
  - "docs/features/accounts-and-sessions/spec.md"
  - "docs/features/accounts-and-sessions/adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md"
  - "docs/features/accounts-and-sessions/sad.md"
  - "docs/features/accounts-and-sessions/test-plan.md"
  - "docs/features/accounts-and-sessions/data-model.md"
  - "docs/adr/0003-authenticate-with-identity-and-an-httponly-cookie.md"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 (re-review) — N-03, N-04, N-07"
status: "todo"
---

# T40 — Record the unknown-address counter limits, the 14 days + 1 hour boundary, and close the ADR 0003 question

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent re-review — N-03, N-04, N-07 in [`_review/review-2026-09-22-2.md`](../_review/review-2026-09-22-2.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** T35, T39 (shared files, see `files_hint`).

## Acceptance criteria

- AC-05b — verbatim in [spec.md §5](../spec.md)
- AC-07 — verbatim in [spec.md §5](../spec.md)
- AC-12 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] spec §6.1 and an ADR 0010 amendment record the unregistered-address counter as accepted residual risk (lost on restart, cap of 100,000 tracked addresses) and sad §5 lists the IUnknownAddressAttempts port; the test-plan.md rows for the AC-07 boundary and the data-model.md fixture state 14 days + 1 hour after the stamp, and sad flow 5 (or an ADR 0008 amendment) records why; the spec §8 ADR-0003 item is ticked with a link to ADR 0010 and ADR 0003 carries a "see ADR 0010" note; no code changes.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
