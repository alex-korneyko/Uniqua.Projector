---
id: T62
title: "Measure the sign-in length guard on the trimmed address against the Domain limits"
layer: "ports"
deps: []
acs: ["AC-04", "AC-05b"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (fourth re-review) — V-01"
status: "todo"
---

# T62 — Measure the sign-in length guard on the trimmed address against the Domain limits

## Place in the sequence

- **Origin:** a verdict from the independent fourth re-review — V-01 in [`_review/review-2026-09-23-3.md`](../_review/review-2026-09-23-3.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- AC-04 — verbatim in [spec.md §5](../spec.md)
- AC-05b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] POST /api/v1/sessions compares the trimmed email's length with Account.MaxEmailLength and the password's length with Account.MaxPasswordLength, so the guard measures the same value registration measured and cannot refuse an owner whose registered address arrives padded with whitespace; the CreateSessionRequest.MaxEmailLength and MaxPasswordLength copies are removed (or become aliases of the Domain constants) so the limits live in Domain only; the refusal for a genuinely over-long credential is unchanged (401 accounts.credentials_invalid, no slot taken); tests: an account registered with a 256-character address signs in with 201 when the address is sent with a trailing space and with a leading newline; a 257-character address after trimming is still refused with the wrong-password body and takes no slot.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
