---
id: T31
title: "Drive the thin path and the visitor board link end to end through the built client with Playwright"
layer: "tests"
deps: ["T30"]
acs: ["AC-01", "AC-12", "AC-27"]
files_hint:
  - "src/Uniqua.Projector.Web/package.json"
  - "src/Uniqua.Projector.Web/package-lock.json"
  - "src/Uniqua.Projector.Web/playwright.config.ts"
  - "src/Uniqua.Projector.Web/e2e/thin-path.spec.ts"
  - "src/Uniqua.Projector.Web/e2e/board-link.spec.ts"
  - "src/Uniqua.Projector.Web/.gitignore"
owner: "Alex Korneiko"
estimate: "L"
status: "todo"
origin: "review 2026-09-26 — findings B3"
---

# T31 — Drive the thin path and the visitor board link end to end through the built client with Playwright

## Place in the sequence

- **Origin:** follow-up from the independent review, findings B3 — [review-2026-09-26.md](../_review/review-2026-09-26.md). The finding text there is the source; this brief restates it.
- **Blocked by:** T30

## What is wrong and what to do

- **B3** — test-plan.md:34 (thin path: register → create board → add a card, AC-01 + AC-12) and :121 (a signed-out visitor opens a board link, signs in, returns to the board; a non-member sees the not-available screen, AC-27) were never built; there is no browser driver. Add `@playwright/test` (Apache-2.0 — permitted by CLAUDE.md; the user approved it at review) as a devDependency in `package.json`; `package-lock.json` updated.
- The flows must run through **one origin**: the API serving the built client from `wwwroot` (`Program.cs:42-56`), so the httpOnly session cookie and antiforgery token behave as in production. Use Playwright `webServer` to build the client into the API's `wwwroot` and start the API against a SQL Server container (e.g. `docker run` of the same image `ApiFactory` uses, in a global setup), with Development-style migrate-on-start. Read `tests/Uniqua.Projector.Api.IntegrationTests/ApiFactory.cs` for the connection-string/config shape and follow it; do not introduce an in-memory provider.
- Add an npm script `test:e2e` (`playwright test`). Keep `vitest run` from picking up `e2e/` (exclude it in the vitest config if needed). Ignore Playwright's output folders.

## Acceptance criteria

Re-read AC-01, AC-12, AC-27 verbatim in [spec.md](../spec.md) §5 before writing the test; the test asserts the business-observable outcome the AC names.

**Fallback:** if this brief is insufficient or contradicted by the code, read the cited file:line, [spec.md](../spec.md), [sad.md](../sad.md), [contracts/openapi.yaml](../contracts/openapi.yaml), [screens.md](../screens.md) and [test-plan.md](../test-plan.md). Do not guess.

## Definition of Done

- [ ] `npm --prefix src/Uniqua.Projector.Web run test:e2e` passes locally against Docker for both flows; each flow asserts the business-observable outcome (the board with its card visible after a reload; the return to the board address; the not-available screen for a non-member). Unit `npm test`, lint and build still clean. Report the licence of every package added to the lockfile.
- [ ] Test written first and seen failing for the right reason (quote the failing line).
- [ ] Every CLAUDE.md rule still holds (layer direction, domain rules in Domain, ProblemDetails from the one handler, Tailwind only, permissive licences).
