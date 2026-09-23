---
id: T54
title: "Bound the sign-in cap's keys: hash the address, reserve per source first, drop emptied lists, and refuse over-long credentials before reserving"
layer: "ports"
deps: []
acs: ["AC-12", "AC-05b"]
files_hint:
  - "src/Uniqua.Projector.Api/Accounts/SignInRateLimit.cs"
  - "src/Uniqua.Projector.Api/Accounts/SlidingWindowLimiter.cs"
  - "src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Accounts/SessionEndpointTests.cs"
  - "tests/Uniqua.Projector.Api.IntegrationTests/Quality/GuessingProtectionTests.cs"
owner: "Alex Korneiko"
estimate: "S"
origin: "review 2026-09-23 (third re-review) — S-01"
status: "todo"
---

# T54 — Bound the sign-in cap's keys: hash the address, reserve per source first, drop emptied lists, and refuse over-long credentials before reserving

## Place in the sequence

- **Origin:** a verdict from the independent third re-review — S-01 in [`_review/review-2026-09-23-2.md`](../_review/review-2026-09-23-2.md). The finding rows there (problem, citations, owner decision) are this task's primary brief; read them first.
- **Blocked by:** —.

## Acceptance criteria

- AC-12 — verbatim in [spec.md §5](../spec.md)
- AC-05b — verbatim in [spec.md §5](../spec.md)

## Definition of Done

- [ ] The per-address key is source + separator + lowercase hex SHA-256 of the normalised email (the same hash InMemoryUnknownAddressAttempts uses), so a key's length is fixed whatever was submitted and no submitted address is held in plain text; SignInRateLimit.Reserve reserves the per-source slot first and the per-address slot second, releasing the per-source slot when the per-address cap refuses, so an attempt the per-source ceiling refuses creates no per-address entry; SlidingWindowLimiter.Release removes a key's list once it is empty (only that exact list, as PruneExpired does); POST /api/v1/sessions refuses an email longer than 256 characters or a password longer than 128 with the same problem a wrong password gets (AccountErrors.CredentialsInvalid, 401), before Reserve and before any account lookup — the refusal depends on input length alone, never on account state, so it adds no AC-05b oracle, and the CreateSessionRequest remark is updated to say why this pre-check is safe; SignInRateLimit exposes the per-address tracked count (e.g. TrackedAddressKeyCount); tests: a flood of distinct addresses (including one of 10,000 characters) from a source already at its per-source ceiling leaves the per-address tracked count unchanged; an over-long email and an over-long password each get the wrong-credentials refusal and consume no slot; the per-address key length is constant for a 5-character and a 10,000-character address; the existing cap tests stay green.
- [ ] The test is written first and seen to fail for the reason the finding names (skipped only for a `docs`/`config` task, whose DoD is checked directly).
- [ ] Every Hard Rule of the original task that owned these files still holds (Domain rules in Domain, ProblemDetails from the one handler, Tailwind only, TanStack Query owns server state).
- [ ] unit + integration + lint + vet clean
