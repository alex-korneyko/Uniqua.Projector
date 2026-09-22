---
id: T31
title: "Keep register, sign-in and sign-out pending until the session is known, and stop truncating passwords"
layer: "ui"
deps: ["T29", "T30"]
acs: ["AC-01", "AC-04", "AC-08"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/auth/RegisterScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/SignInScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/AccountShell.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/useSession.ts"
  - "src/Uniqua.Projector.Web/src/features/auth/__tests__/"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-20, R-21, R-22"
status: "todo"
---

# T31 — Keep register, sign-in and sign-out pending until the session is known, and stop truncating passwords

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-20, R-21, R-22 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** T29, T30 (a shared file, see `files_hint`).

## Acceptance criteria

- AC-01 — verbatim in [spec.md §5](../spec.md)
- AC-04 — verbatim in [spec.md §5](../spec.md)
- AC-08 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] A 201 writes the returned Account into the session cache before invalidating, so the form never re-enables into a visitor state; the registration password has no maxLength; sign-out is disabled and labelled 'Signing out…' while pending (all tested).
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
