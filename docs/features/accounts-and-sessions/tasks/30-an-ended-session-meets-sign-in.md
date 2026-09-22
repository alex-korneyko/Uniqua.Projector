---
id: T30
title: "Present the sign-in form when a known session ends, and treat a 401 from any call as the end of the session"
layer: "ui"
deps: []
acs: ["AC-07", "AC-07b", "AC-10"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/auth/VisitorScreens.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/useSession.ts"
  - "src/Uniqua.Projector.Web/src/api/queryClient.ts"
  - "src/Uniqua.Projector.Web/src/main.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/__tests__/AccountShell.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/__tests__/VisitorScreens.test.tsx"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-08, R-09"
status: "done"
---

# T30 — Present the sign-in form when a known session ends, and treat a 401 from any call as the end of the session

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-08, R-09 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** — (a shared file, see `files_hint`).

## Acceptance criteria

- AC-07 — verbatim in [spec.md §5](../spec.md)
- AC-07b — verbatim in [spec.md §5](../spec.md)
- AC-10 — verbatim in [spec.md §5](../spec.md)

> **Superseded in part, 2026-09-22 (spec §8):** the owner decided that *every* visitor sees the sign-in
> form first, not only one whose session ended. The DoD's "a first visit with no session shows
> registration" no longer holds; a first visit shows sign-in, with registration one click away inside
> the card.

## Definition of Done

- [ ] A composed AccountShell + VisitorScreens test: a 401 on /me refetch while an account is cached shows the sign-in form; sign-out shows the sign-in form; a first visit with no session shows registration; the switch between the two is inside the card; a 401 from a non-/me query clears the session.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
