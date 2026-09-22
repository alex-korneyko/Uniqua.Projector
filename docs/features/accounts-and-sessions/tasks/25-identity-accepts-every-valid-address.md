---
id: T25
title: "Accept every address MailAddress accepts and stop mapping unexpected Identity errors to a uniqueness refusal"
layer: "infra"
deps: ["T21"]
acs: ["AC-02b", "AC-11b"]
files_hint:
  - "src/Uniqua.Projector.Infrastructure/Accounts/IdentityAccountStore.cs"
  - "src/Uniqua.Projector.Infrastructure/DependencyInjection.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/IdentityAccountStoreTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-22 — R-14"
status: "done"
---

# T25 — Accept every address MailAddress accepts and stop mapping unexpected Identity errors to a uniqueness refusal

## Place in the sequence

- **Origin:** a Fix-now verdict from the independent review — R-14 in [`_review/review-2026-09-22.md`](../_review/review-2026-09-22.md). The finding rows there (problem, citations, suggested fix) are this task's primary brief; read them first.
- **Blocked by:** T21 (a shared file, see `files_hint`).

## Acceptance criteria

- AC-02b — verbatim in [spec.md §5](../spec.md)
- AC-11b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] Registering o'brien@example.test and an address with non-ASCII letters succeeds; InvalidUserName is never reported as display_name_taken; an Identity error that is neither duplicate is thrown, not mapped to email_taken.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
