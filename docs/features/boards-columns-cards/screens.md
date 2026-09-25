---
status: draft            # draft | approved
feature_size: "M"
tool: "code"
updated_at: "2026-09-25"
---

# Screens — boards-columns-cards

> The canonical **screen manifest** — every screen in every state — produced by `screens` (between
> `api` and `tasks`) and read by `tasks` (each `ui` task cites SCR ids + states), `implement`
> (builds the screen to the declared states) and `review` (the built screen must match this).
> Downstream stages reference **only this manifest** — never the raw Figma / `.pen` file.

## Source

- **Tool:** code. This is the default because `docs/design-system.md` does not exist, so there is no canon and no `tool` to copy. It is not a fallback from an unavailable MCP. Run `/sdd:design-system` to establish the canon. Until then, the component inventory below is the one this manifest measures reuse against.
- **File:** the wireframes are inline below. There is one per state that needs a layout of its own. A state that only swaps a message on an existing layout is described in its table row.

**Component inventory used (read from the repository, since there is no design-system file):**

| Kind | Name | Where |
|---|---|---|
| Vendored shadcn/ui primitive | `Button` (variants `default`, `outline`, `ghost`; sizes `sm`, `default`, `lg`) | `src/Uniqua.Projector.Web/src/components/ui/button.tsx`, `button-variants.ts` |
| Vendored shadcn/ui primitive | `Card`, `CardHeader`, `CardTitle`, `CardDescription`, `CardContent` | `components/ui/card.tsx` |
| Vendored shadcn/ui primitive | `Input` | `components/ui/input.tsx` |
| Vendored shadcn/ui primitive | `Label` | `components/ui/label.tsx` |
| Feature component (accounts-and-sessions) | `AccountShell`, `SignInScreen`, `RegisterScreen`, `VisitorScreens` | `src/Uniqua.Projector.Web/src/features/auth/` |
| Installed dependency (MIT) | `lucide-react` icons; `@dnd-kit/core` + `@dnd-kit/sortable` (column drag, keyboard sensor included) | `package.json`; dnd-kit is restricted to the board screen by `architecture-map.md` §Frontend |

**Idioms reused from the auth screens.** These are markup patterns, not components. Every screen below uses them by these names:

- **status line**: `<div role="status" aria-live="polite">` with `text-muted-foreground text-sm`, as in `AccountShell`'s «Checking your session…». Used for every loading state.
- **refusal line**: `<p role="alert" className="text-destructive text-sm">`, as in `SignInScreen`. It is placed directly under the control the member acted on.
- **hint line**: `<p id=… className="text-muted-foreground text-xs">` bound with `aria-describedby`, as in `RegisterScreen` («Between 1 and 100 characters.»).
- **failed block**: a heading, one sentence and a `Button` «Try again», as in `AccountShell`'s «We could not reach the server». Used when a read fails.

**Refusal wording (applies to every refusal below).** The text shown is the server's problem `title` followed by its `detail`, the same way `describeRefusal` in `features/auth/accountRefusals.ts` builds it, so the wording lives in one place (`BoardProblems.cs`). The quoted strings in the tables are the contract's examples. The client writes its own text in only four cases:

- «That column no longer exists.» and «That card no longer exists.» — used when a `boards.not_available` answer to a change turns out, after the board is read again, to concern a column or card rather than the board (sad.md §6 flow 2).
- «We could not reach the server. Please check your connection and try again.» — used when there is no answer at all.
- «Something went wrong. Please try again.» — used for any code with no row of its own: `api.unexpected`, `api.request_too_large`, `api.request_rejected`, `boards.request_malformed`, `boards.request_invalid`, `boards.column_position_invalid` (OQ-API-2/3: a correct client never sends these), and `accounts.antiforgery_failed`, where «Please try again.» is appended as `withRetry` does.

Every rate-limit and busy message ends with «Try again in N seconds.», taken from `retry_after_seconds`, as `rateLimitMessage` does.

**Cross-cutting change states.** Every screen that submits a change (SCR-03 to SCR-07) has these four rows. Each screen's table repeats them so it can be read on its own.

| State | Trigger | What the member sees |
|---|---|---|
| rate-limited | 429 `boards.change_rate_limited` (AC-17) | A refusal line at the control: «Changes are temporarily limited. You have made many changes in the past minute. Try again in N seconds.» Typed text stays. Nothing changed. |
| busy | 503 `boards.contended` (OQ-API-1 contract default). Declared on every change except `renameColumn` and `editCard` | A refusal line: «The board is busy. Others are changing this board right now. Try again in 1 second.» Typed text stays. |
| session-ended | 401 `accounts.session_not_recognised` on a change (AC-28, flow 13) | Replaced by SCR-01 `from-ended-session`. Typed text is written to `sessionStorage` under the account id, with the board id (if any) and the item it named. The return address is the current address: the board for SCR-04 to SCR-07, and the board list for SCR-03. |
| error | Any other failure (see Refusal wording) | A refusal line with the client's own text. Typed text stays, and the control stays usable to retry. |

## Screens

### SCR-01 — Visitor sign-in (existing, from accounts-and-sessions)

This screen is reused unchanged. This feature adds no pixels to it, only the routing states around it (ADR 0012, sad.md §8 *Sign-in return address*). The screen's own refusals (wrong credentials, sign-in limit, registration) belong to accounts-and-sessions and are not restated here.

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| default | No session, at any address that is not a board | `VisitorScreens` → `SignInScreen` / `RegisterScreen`, unchanged | existing screen — no new wireframe |
| from-board-link | No session, at `/boards/:boardId`, real or not (AC-27, flow 12) | Same as default, pixel for pixel. The address is remembered as `returnTo`. No board request is made, and nothing about the board is shown | «wireframe below» |
| from-ended-session | A change answered 401 while a board was open (AC-28, flow 13) | Same as default, with no extra message (owner decision 2026-09-25: no AC asks for one). The typed text is already kept in `sessionStorage`. `returnTo` is the board | «wireframe below» |
| success | Signed in or registered | `returnTo` is followed only when it is an in-app path (one leading `/`, not `//`, no scheme). Otherwise, or when there is none, the member lands on SCR-02. At the board address the membership rule decides between SCR-04 and SCR-08. Kept text belonging to a different account is removed from storage and never shown | — (navigation) |
| loading | Session check in flight | `AccountShell` status line «Checking your session…», unchanged | existing |
| error | Session check failed (not a 401) | `AccountShell` failed block «We could not reach the server», unchanged | existing |
| empty | N/A: a form, not a collection | — | — |

```text
SCR-01 from-board-link / from-ended-session — identical to the existing sign-in, by design
+--------------------------------------------+
|              +----------------------+      |
|              | Sign in              |      |
|              | Welcome back.        |      |
|              | Email address [____] |      |
|              | Password      [____] |      |
|              | [      Sign in     ] |      |
|              | No account yet? ...  |      |
|              +----------------------+      |
|   (no board name, no board id, no hint)    |
+--------------------------------------------+
```

### SCR-02 — Board list

Root: `BoardListScreen`, inside `AccountShell`'s signed-in frame. `AccountShell`'s `max-w-md` body is widened for the board screens, and its header with the display name and «Sign out» stays. Data comes from `listMyBoards`.

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| loading | `listMyBoards` in flight | status line «Loading your boards…» | — (status line only) |
| empty | 200 with `items: []` (flow 4, no memberships). This is where a first-time visitor starts the thin path | `Card` with «You have no boards yet.» and a `Button` (default) «Create board» → SCR-03 | «wireframe below» |
| default | 200 with one or more items (AC-04) | Heading «My boards» and a `Button` «Create board» → SCR-03. The list is ordered newest first by `created_at`, as the server sends it. Each row is a `Button` (outline, full width, left-aligned) that goes to `/boards/:id`. The name is shown through `PlainText` (AC-16), with a muted «Owner» marker when `is_owner` | «wireframe below» |
| error | 500, or no answer | failed block «We could not load your boards» + `Button` «Try again» (refetch) | «wireframe below» |
| session-ended | 401 on the read | SCR-01 `from-board-link` with `returnTo` = `/` (no text to keep) | — (navigation) |
| kept-text offer | Back on the list after SCR-01, same account, with a kept board name from SCR-03 (AC-28) | `KeptTextNotice` above the list: «You were signed out before this was saved:», the name as `PlainText`, `Button` «Apply again» (reopens SCR-03 with the name filled in), `Button` (ghost) «Discard» | same layout as the SCR-04 kept-text offer |
| after-delete | Arrives from SCR-07 `success` (AC-20) | The default or empty state, with the deleted board already removed from the cached list. No toast (owner decision 2026-09-25) | same as default |
| validation | N/A: no input on this screen | — | — |

```text
SCR-02 default
+--------------------------------------------------------------+
| Olena K.                                        [ Sign out ] |
|--------------------------------------------------------------|
| My boards                                  [ Create board ]  |
|                                                              |
| [ Q4 launch                                        Owner  ]  |
| [ Test board                                       Owner  ]  |
| [ <script>alert(1)</script>   <- shown literally   Owner  ]  |
+--------------------------------------------------------------+

SCR-02 empty
+--------------------------------------------------------------+
| Olena K.                                        [ Sign out ] |
|--------------------------------------------------------------|
| My boards                                                    |
|   +------------------------------------------------------+   |
|   | You have no boards yet.                              |   |
|   | [ Create board ]                                     |   |
|   +------------------------------------------------------+   |
+--------------------------------------------------------------+

SCR-02 error
+--------------------------------------------------------------+
| We could not load your boards                                |
| Please check your connection and try again.                  |
| [ Try again ]                                                |
+--------------------------------------------------------------+
```

### SCR-03 — Create board

Root: `CreateBoardDialog`, a `NEW: Dialog` over SCR-02. It calls `createBoard`.

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| default | «Create board» on SCR-02 | `Dialog` titled «Create board»; `Label` «Board name» + `Input` (autofocused) + hint line «Between 1 and 100 characters.»; `Button` (default) «Create», `Button` (ghost) «Cancel» → SCR-02 | «wireframe below» |
| pending | Submitted | «Create» disabled, reading «Creating…» | same as default |
| validation | 400 `boards.board_name_invalid` (AC-02, flow 1). The client may run the same check first: the Text rule, measured in code points after trimming Unicode whitespace (sad.md §8) | Refusal line «The board name is not usable. A board name must be between 1 and 100 characters.» under the input. Typed text stays, and focus returns to the input | same as default + refusal line |
| limit | 409 `boards.owned_board_limit_reached` (AC-03) | Refusal line «No more boards can be created. An account can own at most 50 boards.» «Create» is disabled, so «Cancel» is the only way on (flow US-01 → F) | «wireframe below» |
| rate-limited | 429 (AC-17) | Refusal line, as in the cross-cutting table. Name kept | same as default + refusal line |
| busy | 503 `boards.contended` | Refusal line, as in the cross-cutting table. Name kept | same as default + refusal line |
| session-ended | 401 (AC-28) | → SCR-01 `from-ended-session`. The name is kept. After the same account signs in, it lands on SCR-02 with `KeptTextNotice` «Apply again», which reopens this dialog with the name filled in | — (navigation) |
| error | Anything else | Refusal line with the client's text. Name kept | same as default + refusal line |
| success | 201 with the `Board` (AC-01) | The dialog closes and the app goes to `/boards/:id`. The board query is seeded from the 201 body, so SCR-04 `default` shows To do, In progress, Done with no second request | → SCR-04 |
| loading / empty | N/A: nothing is read before the form is used, and the form is not a collection | — | — |

```text
SCR-03 default
+-------------------------------------------+
| Create board                          [x] |
|                                           |
| Board name                                |
| [ Sprint 12______________________ ]       |
| Between 1 and 100 characters.             |
|                                           |
|                    [ Cancel ] [ Create ]  |
+-------------------------------------------+

SCR-03 limit
+-------------------------------------------+
| Create board                          [x] |
| Board name                                |
| [ Sprint 12______________________ ]       |
| Between 1 and 100 characters.             |
| ! No more boards can be created. An       |
|   account can own at most 50 boards.      |
|                    [ Cancel ] [Create]░░  |  <- Create disabled
+-------------------------------------------+
```

### SCR-04 — Board

Root: `BoardScreen` at `/boards/:boardId`, inside `AccountShell`'s widened frame. It reads with `openBoard`. Its changes are `addColumn`, `renameColumn`, `moveColumn`, `deleteColumn`, `addCard` and `renameBoard`. The layout is desktop-first, and the row of columns scrolls sideways on a narrow screen (ux-flows §Platform decisions). Live updates are N/A until roadmap step 8: other members' changes appear on reopen or after a refusal.

**Where a refusal appears.** It appears at the control the member used: under the column header, under the add-column form, under the add-card form or under the board title. The notice region under the board header is used for only two things. One is `KeptTextNotice`, when the control that held the text has gone. The other is the column-move stale refusal, because a drag leaves no form behind.

**Screen and board level**

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| loading | `openBoard` in flight, with no seeded cache | status line «Loading the board…» | — (status line only) |
| error | 500 on `openBoard`, or no answer | failed block «We could not load this board» + `Button` «Try again» | same layout as SCR-02 error |
| not-available | 404 `boards.not_available` on `openBoard` (AC-25, AC-20 after a deletion) | SCR-08 is rendered at the same address | → SCR-08 |
| session-ended (read) | 401 on `openBoard` | SCR-01 `from-board-link` with `returnTo` = this board | — (navigation) |
| default (member) | 200, `is_owner: false` (AC-21) | Header: `Button` (ghost, sm) «← My boards» and the board name as `PlainText` in an `h1`, with no rename or delete controls (AC-22, owner-only actions not offered). Body: one `BoardColumn` per column in `position` order, then the add-column form (`InlineNameEditor` behind a `Button` (outline) «Add column») | «wireframe below» |
| default (owner) | 200, `is_owner: true` | The same, plus in the header a `Button` (ghost, sm) with the lucide `Pencil` icon and aria-label «Rename board», which turns the title into an `InlineNameEditor`, and a `Button` (outline, sm) «Delete board» → SCR-07 | «wireframe below» |
| empty (board) | N/A: a board always keeps at least one column (AC-10), and a new board has three (AC-01) | — | — |
| empty (column) | A column with `cards` = none | `BoardColumn` with an empty card area and only «Add card» | «wireframe below» (Done column) |
| kept-text offer | Back on this board after SCR-01, same account, with kept text for it (AC-28, flow 13 same-account branch) | `KeptTextNotice` in the notice region: «You were signed out before this was saved:», the kept text as `PlainText`, `Button` «Apply again», `Button` (ghost) «Discard». «Apply again» resubmits it as an ordinary change, so it can be refused like any other (stale, gone, limits). The notice is removed from storage once applied or discarded | «wireframe below» |
| kept-text gone | A change's column or card was deleted since it was loaded: 404 on a change, then the board is read again and is still available (AC-18b, flow 2) | The board is refreshed. `KeptTextNotice` shows «That column no longer exists.» or «That card no longer exists.», any typed text as `PlainText` (it can be selected and copied), and `Button` (ghost) «Dismiss». There is no «Apply again», because its target is gone. If the re-read itself answers 404, the member gets SCR-08 | «wireframe below» |

**Board rename (owner) — `InlineNameEditor` in the header, `renameBoard`**

| State | Trigger / condition | Components | Source-ref |
|---|---|---|---|
| editing | Pencil pressed | `InlineNameEditor` with the current name, hint line «Between 1 and 100 characters.», `Button` «Save», `Button` (ghost) «Cancel» | «wireframe below» |
| pending | Saved | «Save» reads «Saving…», disabled | same as editing |
| validation | 400 `boards.board_name_invalid` (AC-02 applied to rename, flow 11) | Refusal line under the editor. Typed name kept | same as editing + refusal line |
| owner-only | 403 `boards.owner_only` (AC-22). The UI does not offer this control to a non-owner, so the request would have to arrive another way | Refusal line «Only the board owner can do this. Only the board owner may rename or delete a board.» The board is read again, and with `is_owner: false` the owner controls disappear | same as editing + refusal line |
| success | 200 `BoardName` (AC-19) | The title shows the new name. The cached board list is patched, so SCR-02 shows it too | default (owner) |
| rate-limited / busy / session-ended / error | See the cross-cutting table | Refusal line under the editor, or → SCR-01 | same as editing + refusal line |

**Add column — `InlineNameEditor` at the end of the row, `addColumn`**

| State | Trigger / condition | Components | Source-ref |
|---|---|---|---|
| editing | «Add column» pressed | `InlineNameEditor` (empty), hint line «Between 1 and 50 characters.», «Add» / «Cancel» | «wireframe below» |
| pending | Submitted | «Add» reads «Adding…», disabled | same as editing |
| validation | 400 `boards.column_name_invalid` (AC-08) | Refusal line «The column name is not usable. A column name must be between 1 and 50 characters.» Typed name kept | same as editing + refusal line |
| limit | 409 `boards.column_limit_reached` (AC-11) | Refusal line with the ceiling («A board can hold at most 20 columns.»). The control stays, and the server is the judge | same as editing + refusal line |
| success | 201 (AC-05) | The new `BoardColumn` appears at the end. The editor closes, and «Add column» is back | default |
| rate-limited / busy / session-ended / error | See the cross-cutting table | Refusal line, or → SCR-01 | same as editing + refusal line |

**Column header — rename, move, delete (inside `BoardColumn`)**

| State | Trigger / condition | Components | Source-ref |
|---|---|---|---|
| column default | — | A drag handle `Button` (ghost, sm, lucide `GripVertical`, aria-label «Move column <name>»); the name as `PlainText`; `Button` (ghost, sm, `Pencil`, «Rename column») and `Button` (ghost, sm, `Trash2`, «Delete column») | «wireframe below» |
| rename editing | Pencil pressed | The name becomes `InlineNameEditor`, hint «Between 1 and 50 characters.» | «wireframe below» |
| rename pending | Saved | «Saving…», disabled | same as rename editing |
| rename validation | 400 `boards.column_name_invalid` (AC-08) | Refusal line under the editor. Typed name kept | same as rename editing + refusal line |
| rename stale | 409 `boards.column_renamed` + `current_column` (AC-06b) | The header data is patched to `current_column`, so its name and `name_version` are current. The editor stays open with the member's typed name, and a refusal line reads «The column was renamed. This column was renamed since you last saw it.» with the current name shown in it through `PlainText`. «Save» now applies the typed name against the new version (flow US-08 → S2 → K) | «wireframe below» |
| rename gone | 404, re-read, board available (AC-18b) | → `kept-text gone`: «That column no longer exists.» with the typed name | see kept-text gone |
| rename success | 200 (AC-06) | The new name is shown, and the editor closes | column default |
| moving | Drag started with the pointer, or with Space or Enter on the handle (dnd-kit keyboard sensor) | dnd-kit `SortableContext` on a horizontal list. The dragged column is lifted, and the others shift to show where it would land | «wireframe below» |
| move pending | Dropped at a new index | The new order is shown optimistically, and further drags are paused until the answer | same as default |
| move stale | 409 `boards.columns_changed` + `current_layout` (AC-24) | The cache is patched to `current_layout`, so the current columns and order are shown. The notice region shows a refusal line «The columns changed since you last saw them.» The member can drag again against the fresh order | «wireframe below» |
| move gone | 404, re-read, board available | The board is refreshed. The notice region shows «That column no longer exists.» (nothing typed, so no kept text) | see kept-text gone |
| move success | 200 with the new column set (AC-06, AC-24b) | The order is kept as dropped, and `column_layout_version` is updated from the answer | default |
| delete pending | Trash pressed. There is no confirmation, because only an empty column can go (ux-flows §Platform decisions) | The trash button is disabled | same as column default |
| delete stale | 409 `boards.column_renamed` (AC-06b on delete, flow 8) | The header is patched to `current_column`. A refusal line under the header reads «This column was renamed since you last saw it.» with its current name. Nothing is deleted | same as column default + refusal line |
| delete not-empty | 409 `boards.column_not_empty` (AC-09) | A refusal line under the header: «A column that still holds cards cannot be deleted.» The column and its cards are untouched. The button is **not** disabled beforehand, because the server's refusal is the AC's message | «wireframe below» |
| delete last-column | 409 `boards.last_column` (AC-10, AC-10b) | A refusal line under the header: «A board must keep at least one column.» | same as column default + refusal line |
| delete gone | 404, re-read, board available | The board is refreshed. The column is already gone, so nothing further is shown | default |
| delete success | 200 with the remaining columns (AC-07) | The column is removed, and the others keep their relative order | default |
| rate-limited / busy / session-ended / error | See the cross-cutting table. `busy` does not apply to rename (not declared on `renameColumn`) | A refusal line under the header or in the editor, or → SCR-01 | same as column default + refusal line |

**Add card — the form at the foot of a `BoardColumn`, `addCard`**

| State | Trigger / condition | Components | Source-ref |
|---|---|---|---|
| closed | — | `Button` (ghost, full width) «+ Add card» | «wireframe below» |
| editing | «Add card» pressed | `Label`+`Input` «Title» with hint «Between 1 and 150 characters.»; `Label`+`NEW: Textarea` «Description (optional)» with hint «At most 10,000 characters.»; «Add» / «Cancel» | «wireframe below» |
| pending | Submitted | «Add» reads «Adding…», disabled | same as editing |
| validation | 400 `boards.card_title_invalid` or `boards.card_description_invalid` (AC-14) | A refusal line naming the limit that failed, under that field. The whole card is refused, and both fields keep their text | same as editing + refusal line |
| limit | 409 `boards.card_limit_reached` (AC-15) | A refusal line with the ceiling («A board can hold at most 1,000 cards.») Text kept | same as editing + refusal line |
| gone | 404, re-read, board available (AC-18b) | → `kept-text gone`: «That column no longer exists.» with the typed title and description | see kept-text gone |
| success | 201 card summary (AC-12) | The title appears last in its column as a card tile (`PlainText`, AC-16). The form clears and stays open for the next card | «wireframe below» |
| rate-limited / busy / session-ended / error | See the cross-cutting table | A refusal line in the form, or → SCR-01 with the title and description kept | same as editing + refusal line |

**Card tile.** A `Button` (outline, full width, `h-auto`, left-aligned, with `whitespace-nowrap` overridden) wraps the title as `PlainText`. Pressing it opens SCR-05. Tiles are ordered by `position` within the column. Descriptions are never shown on the board (ADR 0018).

```text
SCR-04 default (owner) — one column in each sub-state
+------------------------------------------------------------------------------------+
| Olena K.                                                            [ Sign out ]   |
|------------------------------------------------------------------------------------|
| [← My boards]   Q4 launch  [✎]                                  [ Delete board ]   |
| (notice region — empty)                                                            |
|                                                                                    |
| +------------------+ +------------------+ +------------------+ +--------------+    |
| |⋮⋮ To do    [✎][🗑]| |⋮⋮ In progress[✎][🗑]| |⋮⋮ Done     [✎][🗑]| | [ Add column ] |  |
| |------------------| |------------------| |------------------| +--------------+    |
| |[ Write the spec ]| |[ Draw screens   ]| |                  |                     |
| |[ Line one        | |                  | |  (no cards)      |                     |
| |  line two       ]| |                  | |                  |                     |
| |[+ Add card      ]| |[+ Add card      ]| |[+ Add card      ]|                     |
| +------------------+ +------------------+ +------------------+                     |
+------------------------------------------------------------------------------------+
  member (not owner): the [✎] beside the title and [ Delete board ] are absent.

SCR-04 add card — editing, then validation
| +--------------------------------+ |
| |⋮⋮ To do                  [✎][🗑]| |
| |[ Write the spec              ] | |
| | Title                          | |
| | [____________________________] | |
| | Between 1 and 150 characters.  | |
| | ! The card title is not usable.| |
| |   A card title must be between | |
| |   1 and 150 characters.        | |
| | Description (optional)         | |
| | [                            ] | |
| | [                            ] | |
| | At most 10,000 characters.     | |
| |              [Cancel] [ Add ]  | |
| +--------------------------------+ |

SCR-04 column rename — stale (AC-06b)
| +----------------------------------+ |
| |⋮⋮ [ Review______ ] [Save][Cancel] | |
| | Between 1 and 50 characters.      | |
| | ! The column was renamed. This    | |
| |   column was renamed since you    | |
| |   last saw it. Current name:      | |
| |   Doing                           | |
| +----------------------------------+ |

SCR-04 column delete — not-empty (AC-09)
| +----------------------------------+ |
| |⋮⋮ To do                    [✎][🗑]| |
| | ! A column that still holds cards | |
| |   cannot be deleted.              | |
| |[ Write the spec                 ] | |
| +----------------------------------+ |

SCR-04 moving (drag) and move stale (AC-24)
| +---------+   +~~~~~~~~~+   +---------+                                  |
| | To do   |   : Done    :   | In prog |    <- Done lifted, shown where   |
| +---------+   +~~~~~~~~~+   +---------+       it would land              |
| after 409: notice region  "! The columns changed since you last saw them." |
|            and the columns redrawn in current_layout order                 |

SCR-04 kept-text offer (AC-28) / kept-text gone (AC-18b) — notice region
+------------------------------------------------------------------------------+
| You were signed out before this was saved:                                   |
|   Card title: Call the venue                                                 |
|                                          [ Discard ] [ Apply again ]         |
+------------------------------------------------------------------------------+
+------------------------------------------------------------------------------+
| That column no longer exists.                                                |
|   Card title: Call the venue          <- selectable, plain text              |
|                                                          [ Dismiss ]         |
+------------------------------------------------------------------------------+
```

### SCR-05 — Card detail

Root: `CardDetailDialog`, a `NEW: Dialog` over SCR-04. It has no address of its own, and closing it returns to the board as it was. It reads with `openCard` and changes with `editCard`. SCR-06 is a step inside this same `Dialog`.

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| loading | A tile was pressed and `openCard` is in flight | `Dialog` titled with the title from the board summary (`PlainText`), and a status line «Loading the card…» in the body | same as default, body = status line |
| default (reading) | 200 `Card` | The title as `PlainText` in the dialog heading. The description as `PlainText` in a scrollable body, keeping line breaks and repeated spaces, with web addresses not linked (AC-16). An empty description shows a muted «No description». Buttons: `Button` (outline) «Edit», `Button` (ghost) «Delete card» → SCR-06, and the dialog's close button | «wireframe below» |
| error (read) | 500, or no answer, on `openCard` | failed block inside the dialog, «We could not load this card» + «Try again» | same as default, body = failed block |
| gone (read) | 404 on `openCard`, then the board is read again | Board available: the dialog closes, SCR-04 is refreshed, and `KeptTextNotice` shows «That card no longer exists.» (nothing typed yet). Board not available: SCR-08 | → SCR-04 / SCR-08 |
| editing | «Edit» pressed | `Label`+`Input` «Title» with hint «Between 1 and 150 characters.»; `Label`+`NEW: Textarea` «Description» with hint «At most 10,000 characters.»; «Save» / «Cancel» (back to reading) | «wireframe below» |
| pending | Saved | «Save» reads «Saving…», disabled | same as editing |
| validation | 400 `boards.card_title_invalid` or `boards.card_description_invalid` (AC-14) | A refusal line under the field that failed. Both fields keep their text | same as editing + refusal line |
| stale | 409 `boards.card_changed` + `current_card` (AC-23) | The card query and the tile are patched to `current_card`. Above the editor, a `Card` block «Current version» shows its title and description as `PlainText`. A refusal line reads «The card was changed. This card was changed since you opened it.» The member's typed text stays in the fields, and «Save» applies it against the new `content_version` | «wireframe below» |
| gone (save) | 404, re-read, board available (AC-18b) | The dialog closes, SCR-04 is refreshed, and `KeptTextNotice` shows «That card no longer exists.» with the typed title and description | → SCR-04 kept-text gone |
| success | 200 (AC-13) | Back to reading, with the saved text shown and the tile on SCR-04 patched | default (reading) |
| rate-limited / session-ended / error | See the cross-cutting table | A refusal line above the buttons, or → SCR-01 with the title and description kept | same as editing + refusal line |
| busy | N/A: the contract does not declare 503 on `editCard`. A lost write there is the ordinary stale refusal (OQ-API-1) | — | — |
| empty | Covered by default: an empty description shows «No description». A card always has a title | — | — |

```text
SCR-05 default (reading)
+---------------------------------------------------+
| Call the venue                                [x] |
|---------------------------------------------------|
| Ask about the 12th.                               |
| Two   spaces kept.                                |
| https://example.com  <- plain text, not a link    |
| <b>not bold</b>      <- shown literally           |
|---------------------------------------------------|
| [ Delete card ]                         [ Edit ]  |
+---------------------------------------------------+

SCR-05 editing
+---------------------------------------------------+
| Edit card                                     [x] |
| Title                                             |
| [ Call the venue_______________________ ]         |
| Between 1 and 150 characters.                     |
| Description                                       |
| [ Ask about the 12th.                   ]         |
| [                                       ]         |
| At most 10,000 characters.                        |
|                               [ Cancel ] [ Save ] |
+---------------------------------------------------+

SCR-05 stale (AC-23)
+---------------------------------------------------+
| Edit card                                     [x] |
| +-----------------------------------------------+ |
| | Current version                               | |
| | Call the venue, edited elsewhere              | |
| | (No description)                              | |
| +-----------------------------------------------+ |
| ! The card was changed. This card was changed     |
|   since you opened it.                            |
| Title        [ Call the venue — Friday_____ ]     |
| Description  [ Ask about the 12th.          ]     |
|                               [ Cancel ] [ Save ] |
+---------------------------------------------------+
```

### SCR-06 — Confirm card deletion

Root: a confirmation step inside `CardDetailDialog`. The `Dialog` swaps its content, and no second dialog is stacked (owner decision 2026-09-25: one focus trap). It calls `deleteCard` with the `content_version` it was opened at.

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| default | «Delete card» on SCR-05 | Heading «Delete this card?», the card title as `PlainText`, «This cannot be undone.», `Button` (`destructive`, NEW variant) «Delete card», `Button` (ghost) «Cancel» → SCR-05 reading | «wireframe below» |
| pending | Confirmed | «Delete card» reads «Deleting…», disabled | same as default |
| stale | 409 `boards.card_changed` + `current_card` (AC-23) | Back to SCR-05 reading, patched to `current_card`, with a refusal line «This card was changed since you opened it.» so the member can decide again. Nothing is deleted | → SCR-05 + refusal line |
| gone | 404, re-read, board available (AC-18b) | The dialog closes, SCR-04 is refreshed, and `KeptTextNotice` shows «That card no longer exists.» (nothing typed) | → SCR-04 |
| success | 204 (AC-18) | The dialog closes. The tile leaves its column, and the other tiles keep their order | → SCR-04 default |
| rate-limited / busy | See the cross-cutting table (503 is declared on `deleteCard`) | A refusal line above the buttons | same as default + refusal line |
| session-ended | 401 (AC-28) | → SCR-01. There is nothing to keep, because a deletion carries no typed text | — (navigation) |
| error | Anything else | A refusal line above the buttons | same as default + refusal line |
| loading / empty | N/A: shown from an already loaded card, and not a collection | — | — |

```text
SCR-06 default
+---------------------------------------------------+
| Delete this card?                             [x] |
|                                                   |
| Call the venue                                    |
| This cannot be undone.                            |
|                                                   |
|                    [ Cancel ] [ Delete card ▓▓ ]  |  <- destructive
+---------------------------------------------------+
```

### SCR-07 — Confirm board deletion

Root: `DeleteBoardDialog`, a `NEW: Dialog` over SCR-04, offered only when `is_owner`. It calls `deleteBoard` with `confirm_name` in the body.

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| default | «Delete board» on SCR-04 (owner) | `Dialog` titled «Delete board». The text «This deletes the board with all its columns and cards. It cannot be undone.» The board's name as `PlainText` in bold. `Label` «Type the board's name to confirm» + `Input`. `Button` (`destructive`) «Delete board», **always enabled**, because the server compares the name (AC-20, flow US-06 → M1). `Button` (ghost) «Cancel» → SCR-04 | «wireframe below» |
| pending | Confirmed | «Deleting…», disabled | same as default |
| mismatch | 409 `boards.confirmation_mismatch` + `current_name` (AC-20b) | A refusal line «The name does not match. Type the board's current name exactly to delete it.» The displayed name is replaced by `current_name`, and the board cache is patched with it. The typed text stays so the owner can type again | «wireframe below» |
| owner-only | 403 `boards.owner_only` (AC-22) | A refusal line with the owner-only text. The dialog closes when the board is read again and comes back with `is_owner: false` | same as default + refusal line |
| not-available | 404 on `deleteBoard` (the board is already gone, for example deleted in another tab) | SCR-08 | → SCR-08 |
| success | 204 (AC-20) | The app goes to SCR-02. The board is removed from the list cache, and its board query is dropped | → SCR-02 after-delete |
| rate-limited / busy / error | See the cross-cutting table | A refusal line above the buttons. Typed text kept | same as default + refusal line |
| session-ended | 401 (AC-28) | → SCR-01. The typed name is kept. After the same account signs in, `KeptTextNotice` on SCR-04 offers «Apply again», which **only reopens this dialog with the name filled in** and never deletes on its own (owner decision 2026-09-25) | — (navigation) |
| loading / empty | N/A: the name comes from the loaded board, and the dialog is not a collection | — | — |

```text
SCR-07 default
+-----------------------------------------------------------+
| Delete board                                          [x] |
|                                                           |
| This deletes the board with all its columns and cards.    |
| It cannot be undone.                                      |
|                                                           |
| **Q4 launch**                                             |
| Type the board's name to confirm                          |
| [_______________________________________]                 |
|                                                           |
|                         [ Cancel ] [ Delete board ▓▓ ]    |
+-----------------------------------------------------------+

SCR-07 mismatch (AC-20b) — board renamed since the dialog opened
+-----------------------------------------------------------+
| Delete board                                          [x] |
| This deletes the board with all its columns and cards.    |
| It cannot be undone.                                      |
| **Q4 launch, renamed**            <- current_name         |
| Type the board's name to confirm                          |
| [ Q4 launch_____________________________]  <- kept        |
| ! The name does not match. Type the board's current name  |
|   exactly to delete it.                                   |
|                         [ Cancel ] [ Delete board ▓▓ ]    |
+-----------------------------------------------------------+
```

### SCR-08 — Board not available

Root: `BoardNotAvailable`, rendered at the board's own address in place of SCR-04. It shows one identical answer for a board the account is not a member of, a board that never existed and a deleted board (AC-25, AC-20).

| State | Trigger / condition | Components (from the inventory) | Source-ref |
|---|---|---|---|
| default | 404 `boards.not_available` on `openBoard`, or on the board re-read that follows a 404 to a change or a card read | `Card`: `CardTitle` «Board not available», `CardDescription` «This board does not exist, or you are not a member of it.» (the contract's `detail`), and `Button` «Back to my boards» → SCR-02. No name, no id and no hint of which case applies. `document.title` carries no board name either | «wireframe below» |
| loading | N/A: this screen is rendered from a refusal that has already arrived and reads nothing | — | — |
| empty | N/A: it has no collection | — | — |
| error | N/A: it makes no request of its own that could fail. A failure of the read that would have led here is SCR-04 `error` | — | — |

```text
SCR-08 default — identical for all three cases
+--------------------------------------------------------------+
| Olena K.                                        [ Sign out ] |
|--------------------------------------------------------------|
|   +------------------------------------------------------+   |
|   | Board not available                                  |   |
|   | This board does not exist, or you are not a member   |   |
|   | of it.                                               |   |
|   | [ Back to my boards ]                                |   |
|   +------------------------------------------------------+   |
+--------------------------------------------------------------+
```

## New components

| Component | Why no existing primitive fits | Registered in design-system |
|---|---|---|
| `Dialog` (shadcn/ui, vendored as source under `components/ui/dialog.tsx`; adds `@radix-ui/react-dialog`, MIT) | SCR-03, SCR-05/06 and SCR-07 open over the current screen (ux-flows §Platform decisions). `Card` has no focus trap, no Esc handling and no `aria-modal`, and writing those by hand is the risk a vendored primitive removes | pending |
| `Textarea` (shadcn/ui, vendored as `components/ui/textarea.tsx`; no dependency) | A card description is multi-line, up to 10,000 characters. `Input` is a single-line `<input>` | pending |
| `Button` variant `destructive` (edited in place in `button-variants.ts`, using the existing `--color-destructive` token) | SCR-06 and SCR-07 confirm deletions that cannot be undone. The three existing variants do not signal that | pending |
| `PlainText` (feature, `features/board/`) | This is the AC-16 rule in one place: React text nodes only, `whitespace-pre-wrap break-words`, never `dangerouslySetInnerHTML`, never linkified. sad.md §8 *Text rendering* wants it proved by one component test instead of at every call site | pending |
| `InlineNameEditor` (feature) | It is used three times, for board rename, column add and column rename. It combines `Input` with a hint line, a refusal line, Save/Cancel and kept text. No primitive holds edit/save/cancel state | pending |
| `BoardColumn` (feature, named in sad.md §5) | This is a dnd-kit sortable item: the header with handle, rename and delete, the card tiles and the add-card form. It is a board-specific composite with no primitive equivalent | pending |
| `KeptTextNotice` (feature) | It keeps typed text visible when the control that held it is gone (AC-18b), or across a sign-in (AC-28). It is built from `Card` and `Button`, and is one component so that both criteria show the text the same way and never drop it silently | pending |
| Screen roots `BoardListScreen`, `CreateBoardDialog`, `BoardScreen`, `CardDetailDialog`, `DeleteBoardDialog`, `BoardNotAvailable` (named in sad.md §5) | These are the SCR-02 to SCR-08 roots. They are listed only so every name used above is accounted for. They are screens, not reusable components | n/a — screen roots |
