# Tracker — boards-columns-cards

> Status of every task in the epic. `implement` updates `done` as it commits each task.
> States: `todo` · `in_progress` · `blocked` · `review` · `done`.

| # | Task | Layer | Owner | Estimate | Blocked by | Status |
|---|---|---|---|---|---|---|
| T1 | Build the Board aggregate with the Text rule, board creation and the owner-only rules | domain | Alex Korneiko | M | — | done |
| T2 | Give the Board its column rules: add, rename, move and delete with dense positions and version checks | domain | Alex Korneiko | M | T1 | done |
| T3 | Admit, edit and delete cards through the Board's counters with the content-version check | domain | Alex Korneiko | S | T1 | done |
| T4 | Map the boards entities in EF Core and generate the CreateBoards migration | migration | Alex Korneiko | S | T1 | done |
| T5 | Declare the board ports and implement the member-scoped store with the bounded concurrency retry | infra | Alex Korneiko | L | T1, T4 | done |
| T6 | Write the board use cases: create, list, open, rename and delete | app | Alex Korneiko | M | T5 | done |
| T7 | Write the column use cases: add, rename, move and delete, with races re-decided | app | Alex Korneiko | M | T2, T5 | done |
| T8 | Write the card use cases: add, open, edit and delete through the board | app | Alex Korneiko | M | T3, T5 | done |
| T9 | Expose the board endpoints with lenient binding and map every board refusal to one problem document | ports | Alex Korneiko | L | T6 | done |
| T10 | Expose the column endpoints: add, rename, move and delete | ports | Alex Korneiko | M | T7, T9 | done |
| T11 | Expose the card endpoints: add, open, edit and delete | ports | Alex Korneiko | M | T8, T9 | done |
| T12 | Limit each account to 120 board change attempts per rolling minute, before the membership check | ports | Alex Korneiko | S | T9 | todo |
| T13 | Prove the membership boundary across every read and change kind, and map the four board rules to their tests | tests | Alex Korneiko | M | T10, T11, T12, T14 | todo |
| T14 | Prove the board invariants under 1,000 simultaneous pairs and the column order under 1,000 random sequences | tests | Alex Korneiko | L | T10, T11 | todo |
| T15 | Measure opening a full board, a single change and change throughput against the §6 budgets | tests | Alex Korneiko | S | T10, T11, T12 | todo |
| T16 | Vendor Dialog and Textarea, add the destructive Button variant, and build PlainText and KeptTextNotice | ui | Alex Korneiko | S | — | todo |
| T17 | Write the typed boards API client, its query keys and cache patches, and the board refusal wording | ui | Alex Korneiko | M | — | todo |
| T18 | Give the client board addresses with React Router, a guarded return address, and typed text kept across a sign-in | ui | Alex Korneiko | M | — | todo |
| T19 | Build the board list (SCR-02) and the create-board dialog (SCR-03) | ui | Alex Korneiko | M | T16, T17, T18 | todo |
| T20 | Build the board screen (SCR-04) with its owner header, card tiles and add-card form, and the not-available screen (SCR-08) | ui | Alex Korneiko | L | T16, T17, T18 | todo |
| T21 | Let members add, rename, drag and delete columns on the board, with every refusal shown in place | ui | Alex Korneiko | L | T20 | todo |
| T22 | Build the card detail dialog with edit and delete (SCR-05, SCR-06) and the board deletion dialog (SCR-07) | ui | Alex Korneiko | M | T20 | todo |

**Total:** 22 tasks, ~16.5 person-days (S = ½ day, M = ¾ day, L = 1 day).
