---
id: T30
title: "Fix the add-card refusal after Cancel, strengthen the weak client tests, and bound the list screens under a full-width shell"
layer: "ui"
deps: ["T29"]
acs: ["AC-02", "AC-17", "AC-21", "AC-27"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/boards/AddCardForm.tsx"
  - "src/Uniqua.Projector.Web/src/app/__tests__/routes.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/CreateBoardDialog.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/BoardScreen.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/ColumnManagement.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/auth/AccountShell.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/BoardListScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/BoardNotAvailable.tsx"
owner: "Alex Korneiko"
estimate: "M"
status: "todo"
origin: "review 2026-09-26 — findings Q4a, Q4b, Q4c, B7c, Q5"
---

# T30 — Fix the add-card refusal after Cancel, strengthen the weak client tests, and bound the list screens under a full-width shell

## Place in the sequence

- **Origin:** follow-up from the independent review, findings Q4a, Q4b, Q4c, B7c, Q5 — [review-2026-09-26.md](../_review/review-2026-09-26.md). The finding text there is the source; this brief restates it.
- **Blocked by:** T29

## What is wrong and what to do

- **Q4a** — `AddCardForm.tsx:61,81-85,124-134`: `refusal` comes straight from `add.error` and `close()` does not clear it, so reopening «+ Add card» shows the previous refusal over empty fields. Add a `refusalShown` flag reset on open/close (as `AddColumnForm` does). SCR-04 says the button reads «Adding…» while pending: render `{add.isPending ? 'Adding…' : 'Add'}` on the button, as `InlineNameEditor` does.
- **Q4b** — `routes.test.tsx:164` uses `toHaveTextContent('/')`, which matches `//evil.example` itself. Assert the exact path. Add `'/\t/evil'`, `'/\u0000x'` (unsafe) and `'/%2F%2Fevil'` (stays in-app, safe) to the table at `:22-32`.
- **Q4c** — `CreateBoardDialog.test.tsx:172-186` submits a 101-char name, refused client-side, so the mocked 400 `boards.board_name_invalid` is never reached; typing 101 chars also times out in the full run. Submit a name the client accepts so the 400 produces the alert; assert `fetchMock` was called and text + focus are kept; paste rather than type any long value.
- **B7c** — AC-17 has no board-screen proof: add a test that a 429 on a board-screen change shows the rate-limited wording in place and keeps the text. AC-21: `BoardScreen.test.tsx:216-237` (member default) asserts owner controls absent but never that the column and card controls are present — assert them.
- **Q5** — the user's uncommitted working-tree change removes `max-w-5xl` from `AccountShell.tsx:96` so the board (SCR-04) gets the full width. Apply that same change in this task (remove `max-w-5xl`, and remove the now-dead `mx-auto`), update the comment at `:95`, and give `BoardListScreen` and `BoardNotAvailable` their own `mx-auto w-full max-w-3xl` bound so SCR-02 and SCR-08 stay readable. Tailwind utilities only.

## Acceptance criteria

Re-read AC-02, AC-17, AC-21, AC-27 verbatim in [spec.md](../spec.md) §5 before writing the test; the test asserts the business-observable outcome the AC names.

**Fallback:** if this brief is insufficient or contradicted by the code, read the cited file:line, [spec.md](../spec.md), [sad.md](../sad.md), [contracts/openapi.yaml](../contracts/openapi.yaml), [screens.md](../screens.md) and [test-plan.md](../test-plan.md). Do not guess.

## Definition of Done

- [ ] Component tests (RED first where behaviour changes; strengthened tests must be shown failing against the old assertion) pass; the full `npm test` run passes with no timeout; `npm run lint` and `npm run build` clean.
- [ ] Test written first and seen failing for the right reason (quote the failing line).
- [ ] Every CLAUDE.md rule still holds (layer direction, domain rules in Domain, ProblemDetails from the one handler, Tailwind only, permissive licences).
