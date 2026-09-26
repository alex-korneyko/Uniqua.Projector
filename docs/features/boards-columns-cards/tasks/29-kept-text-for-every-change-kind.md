---
id: T29
title: "Keep typed text across a session end for every change kind, and keep a rename whose column was deleted"
layer: "ui"
deps: []
acs: ["AC-28", "AC-18b"]
files_hint:
  - "src/Uniqua.Projector.Web/src/features/boards/BoardScreen.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/CardDetailDialog.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/DeleteBoardDialog.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/ColumnHeader.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/AddColumnForm.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/BoardScreen.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/CardDetailDialog.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/DeleteBoardDialog.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/ColumnManagement.test.tsx"
owner: "Alex Korneiko"
estimate: "M"
status: "todo"
origin: "review 2026-09-26 — findings B1, B2"
---

# T29 — Keep typed text across a session end for every change kind, and keep a rename whose column was deleted

## Place in the sequence

- **Origin:** follow-up from the independent review, findings B1, B2 — [review-2026-09-26.md](../_review/review-2026-09-26.md). The finding text there is the source; this brief restates it.
- **Blocked by:** —

## What is wrong and what to do

- **B1 (AC-28)** — `offeredItems` (`BoardScreen.tsx:231-234`, filter at `:252`) offers back only `add_card` and `rename_board`. Add-column and rename-column already `keep()` (`AddColumnForm.tsx:37`, `ColumnHeader.tsx:68-72` → `BoardScreen.tsx:94`) but their drafts are never shown; `peekKeptDraft` puts them back and hides them. The card edit dialog (`CardDetailDialog.tsx:98-107`) and board deletion dialog (`DeleteBoardDialog.tsx:50-73`) never keep anything. Required by screens.md:52 (cross-cutting session-ended row), :345 (SCR-05 title and description kept), :432 (SCR-07 typed name kept, «Apply again» reopens this dialog with it filled in). Keep drafts from both dialogs through the screen's `onRefused`; offer `add_column`, `rename_column` (resubmit with the column's current `name_version`), `edit_card` (reopen/resubmit with the card's current version) and `delete_board` (Apply again only reopens `DeleteBoardDialog` with the name filled in — never deletes by itself). A draft for a different account is discarded, never shown (existing `draftStore` rule).
- **B2 (AC-18b)** — `goneItemOf('rename_column')` is undefined (`BoardScreen.tsx:95-99,236-238`), and `targetIsGone` (`:292-297`) knows only add-card, so a rename whose column another member deleted shows no «That column no longer exists.» notice and the re-read removes the editor with the typed name (screens.md:225). Map `rename_column → 'column'`, make `targetIsGone` check `fields.column_id` for it.

## Acceptance criteria

Re-read AC-28, AC-18b verbatim in [spec.md](../spec.md) §5 before writing the test; the test asserts the business-observable outcome the AC names.

**Fallback:** if this brief is insufficient or contradicted by the code, read the cited file:line, [spec.md](../spec.md), [sad.md](../sad.md), [contracts/openapi.yaml](../contracts/openapi.yaml), [screens.md](../screens.md) and [test-plan.md](../test-plan.md). Do not guess.

## Definition of Done

- [ ] BoardScreen-level component tests (RED first, mocked API) for a 401 on each of the six text-carrying change kinds: sign-in form shown, text kept, offered back after signing in as the same account, discarded for a different account; and for a rename whose column was deleted: the notice is shown with the typed name. `ColumnManagement.test.tsx:339` extended beyond "onRefused fired". `npm test`, `npm run lint`, `npm run build` clean.
- [ ] Test written first and seen failing for the right reason (quote the failing line).
- [ ] Every CLAUDE.md rule still holds (layer direction, domain rules in Domain, ProblemDetails from the one handler, Tailwind only, permissive licences).
