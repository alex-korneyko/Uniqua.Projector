---
id: T36
title: "Render the sign-in-rate-limited refusal on the sign-in screen with its statement and wait"
layer: "ui"
deps: ["T35"]
acs: ["AC-12"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/auth/accountRefusals.ts"
  - "src/Uniqua.Projector.Web/src/features/auth/SignInScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/__tests__/SignInScreen.test.tsx"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 (re-review) — N-01 (UI half)"
status: "done"
---

# T36 — Render the sign-in-rate-limited refusal on the sign-in screen with its statement and wait

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent re-review — N-01 (UI half) in [`_review/review-2026-09-22-2.md`](../_review/review-2026-09-22-2.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** T35 (shared files, see `files_hint`).

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] A 429 `accounts.sign_in_rate_limited` on the sign-in screen shows its title as the statement and its detail as the reason via statementAndReason, names the wait once, and keeps the form usable afterwards; it is never shown as "credentials invalid" and never ends the session; a component test in SignInScreen.test.tsx drives the 429 through the real api client.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
