---
id: T43
title: "Move keyboard focus to the new form when switching between sign-in and registration"
layer: "ui"
deps: ["T36"]
acs: ["AC-01"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/auth/VisitorScreens.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/RegisterScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/SignInScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/__tests__/VisitorScreens.test.tsx"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 (re-review) — N-12"
status: "todo"
---

# T43 — Move keyboard focus to the new form when switching between sign-in and registration

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent re-review — N-12 in [`_review/review-2026-09-22-2.md`](../_review/review-2026-09-22-2.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** T36 (shared files, see `files_hint`).

## Acceptance criteria

- AC-01 — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] Activating "No account yet? Create one" or its sign-in counterpart moves focus to the new screen's heading or first field (not on the landing's first mount); VisitorScreens.test.tsx asserts toHaveFocus() after each switch; Tailwind only, no new dependency.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
