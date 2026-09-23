---
id: T61
title: "Bring spec, ADR 0010, sad, openapi, test-plan, session rules and README in line with the bounded, re-keyed cap"
layer: "docs"
deps: ["T54", "T55", "T57", "T58"]
acs: ["AC-12", "AC-05b"]
files_hint:
  - "docs/features/accounts-and-sessions/spec.md"
  - "docs/features/accounts-and-sessions/adr/0010-replace-account-lockout-with-a-progressive-per-account-delay.md"
  - "docs/features/accounts-and-sessions/sad.md"
  - "docs/features/accounts-and-sessions/contracts/openapi.yaml"
  - "docs/features/accounts-and-sessions/test-plan.md"
  - "docs/session-rules.md"
  - "README.md"
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/SessionRulesDocumentTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (third re-review) — S-02, S-03, S-05"
status: "todo"
---

# T61 — Bring spec, ADR 0010, sad, openapi, test-plan, session rules and README in line with the bounded, re-keyed cap

## Place in the sequence

- **Origin:** a verdict from the independent third re-review — S-02, S-03, S-05 in [`_review/review-2026-09-23-2.md`](../_review/review-2026-09-23-2.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** T54, T55, T57, T58.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-05b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] spec §6.1 and the ADR 0010 amendment say the per-address ceiling counts (source, address) pairs shared by every source, and that past it the per-source figure of 100 is the fallback; AC-12, the spec §6 row and test-plan.md:66 read '…after 15 minutes in which no failed attempt reached verification'; one log level is stated for an unresolved source, matching the code, in test-plan, spec and ADR 0010; openapi's Retry-After wording covers both the per-pair and per-source refusals; openapi's 429 and sad flow 4 name the unknown-source exemption; openapi declares the over-long-credential refusal T54 added and the renamed api.unexpected from T58; docs/session-rules.md counts attempts in flight and says an IPv6 caller is keyed by its /64; the README names the per-source ceiling and its half of the residual; the log value consequence=rate_limits_shared_by_all_callers is corrected to what actually happens (registration shares one key, sign-in skips its caps); sad §7's per-cap breakdown names the cap= field T57 added; SessionRulesDocumentTests checks the /64 and in-flight wording.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
