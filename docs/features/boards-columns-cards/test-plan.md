---
status: Draft
owner: "Alex Korneiko"
reviewers: ["Alex Korneiko", "Tech Lead"]
updated_at: "2026-09-25"
feature_size: "M"
---

# Test plan — boards-columns-cards

A signed-in account must get from nothing to a board holding a card unaided, and the board's four rules must hold where a reviewer can watch them from outside. The rules are: one identical refusal for a non-member, every named column and card belonging to the board that was checked, no deleting a non-empty or the last column, and no change applied over a newer one. They hold for the owner and equally for a member who is not the owner, and they keep holding when changes arrive at the same moment.

## Levels

| Level | Scope | Strategy (generic — no tool names) |
|---|---|---|
| Unit | The rules the `Board` aggregate owns: the Text rule (trimming Unicode whitespace, counting code points), every name, title and description bound, the four ceilings (50 owned boards, 20 columns, 1,000 cards), dense column positions, the non-empty and last-column rules, the owner-only rule, the deletion confirmation match, and the three version counters of ADR 0016 against the spec §5 stale-change rule. | In memory, through the aggregate's own factory methods, in `tests/Uniqua.Projector.Domain.Tests/Boards/`. No store and no application boot. This is where each of the four rules gets the test a reviewer finds by name. |
| Integration | The application booted through its real entry point against the real store: the member-scoped load that every board use case starts with (ADR 0014), the board concurrency token and its bounded retry (ADR 0015), the one problem document every refusal becomes, the per-account change limit, and the session gate in front of all of it. | An ephemeral real dependency: the throwaway database container the suite already starts once and tears down after, migrated from this feature's own migrations, with the clock port under the test's control. The store is never mocked. The ceilings under simultaneous changes are true only because of the concurrency token, and a mock does not have one. |
| Component *(web surface)* | The screen states `screens.md` declares for SCR-01 to SCR-08: the refusal shown for each problem code, the text kept after every refusal, the cache patched from a stale answer, owner-only controls absent for a member, text rendered as literal characters, and text kept across an ended session. | Rendered in the component harness the repository already uses for the auth screens, against a stubbed transport that answers with the contract's problem documents. Output and surviving field values are asserted with no application boot. |
| E2E-through-UI *(web surface)* | Two flows driven through the built client in a real browser, served same-origin by the API: the thin path (ux-flows.md US-01 → US-04) and a visitor following a board link (ux-flows.md US-10). | The client build served from the API's `wwwroot` against the throwaway database container, as in production, so the httpOnly session cookie and the return address are exercised for real. The browser driver is picked by `implement`, and it must be permissively licensed. |
| Contract | <!-- N/A: not selected at the level-confirmation step. contracts/openapi.yaml and both of its participants live in one repository, and the api stage's drift report holds the shape; the component rows stub the transport with the contract's own problem documents. Revisit if the client is ever built or released separately. --> |
| E2E | <!-- N/A: not selected at the level-confirmation step. The integration rows already boot the whole application through its real entry point against the real store, so an API-only end-to-end suite would repeat them. The one flow no API test can see, the client and API composed on one origin, is covered at e2e-through-UI instead. --> |
| Load | The three numeric §6 targets that are latency and throughput: opening a full board, a single change, and changes per second across boards. | The load tool already in the repo, or e.g. k6 or Locust. Today the repo's latency regression checks live beside the integration tests (`Quality/LatencyBudgetTests.cs`). |
| Visual-regression | <!-- N/A: not selected at the level-confirmation step. screens.md is still status draft and no docs/design-system.md canon exists, so a baseline taken now would pin whatever happened to render first, and every Tailwind edit would break it. Revisit once the canon is established and screens.md is approved. --> |

## AC coverage

| AC (spec.md §5) | Test name (intent-based) | Level | Expected outcome |
|---|---|---|---|
| AC-01 happy path | a new board is owned by its creator and starts with To do, In progress and Done | unit | the name is stored trimmed, the creator is its owner and only member, and the three columns sit at positions 0, 1 and 2 in that order |
| AC-01 happy path | creating a board writes its owner, its three columns and the owned-board count, and returns it opened | integration | one board, one owner membership, three columns and an owned count raised by one; the answer carries the board with its three columns |
| AC-01 happy path | two boards of one account may share a name | integration | both are created and both appear in the account's list |
| AC-01 happy path | a created board opens straight away with no second read | component | the create dialog closes, the board screen shows To do, In progress and Done, and no read of the board is made |
| AC-01 + AC-12 happy path | a fresh account reaches a board holding a card unaided | e2e-through-UI | register → create a board → add a card; the card is shown last in its column and is still there after a reload |
| AC-02 error | a board name is bounded by the Text rule | unit | empty after trimming (including a tab and a non-breaking space) refused; 1 and 100 code points accepted; 101 refused; 100 emoji accepted |
| AC-02 error | an unusable board name is refused and nothing is written | integration | refused with the 1–100 message; no board, no membership, owned count unchanged; the same on rename (AC-19) |
| AC-02 error | the board-name refusal keeps what was typed | component | the 1–100 message is shown under the field and the field still holds the typed name |
| AC-03 domain invariant | an account's 51st owned board is refused by the board rules | unit | the 50th is admitted and the 51st refused, naming the 50-board limit |
| AC-03 domain invariant | an account owning 50 boards is refused another | integration | refused with the 50-board message; nothing written |
| AC-03 domain invariant | simultaneous creates at 49 owned boards admit exactly one | integration | with contention forced on the owned-board counter, one create succeeds and the rest are refused; the account ends with exactly 50 |
| AC-03 domain invariant | the owned-board limit disables Create | component | the 50-board message is shown, Create is disabled, and Cancel is the only way on |
| AC-04 happy path | the board list holds every board the account is a member of and no other | integration | owned boards and boards joined through the membership fixture both appear, newest first, owned ones marked; another account's board does not appear |
| AC-04 happy path | the board list shows its empty, populated and failed states | component | "You have no boards yet." with Create; rows with an Owner mark in the order the server sent; the failed block with Try again |
| AC-05 happy path | an added column takes the next position at the end | unit | the column lands at position n and a duplicate name on the same board is accepted |
| AC-05 happy path | an added column is stored at the end and returned on the next open | integration | the next open lists it last |
| AC-05 happy path | adding a column appends it and closes the editor | component | the new column is drawn last and "Add column" is back |
| AC-06 happy path | a moved column takes its new index and the others close up | unit | positions stay 0…n-1 with no gap and no duplicate after every move |
| AC-06 happy path | a renamed or moved column is seen that way by every member who next opens the board | integration | a second member, joined through the fixture, opens the board and sees the new name and order |
| AC-06 happy path | a column can be moved by pointer and by keyboard | component | a drag, or Space/Enter on the handle, submits the new index and shows the order at once; rename success shows the new name |
| AC-06b domain invariant | renaming or deleting a column against an outdated name version is refused | unit | refused with the column's current name; a rename moves the name version, and adding or changing a card does not |
| AC-06b domain invariant | a column rename or delete from an outdated view is refused and nothing changes | integration | another member, and separately the same account in a second session, renames the column first; the stale rename and the stale delete are both refused, the answer carries the current name, and the store holds the first rename |
| AC-06b domain invariant | a stale column rename keeps the typed name beside the current one | component | the editor stays open with the member's typed name and the refusal shows the current name; on a stale delete the column is not removed |
| AC-07 happy path | deleting an empty column keeps the others' relative order | unit | the remaining positions are 0…n-2 in their previous relative order |
| AC-07 happy path | an empty column is deleted and the rest keep their order | integration | the column is gone from the store and the next open shows the others in their previous order |
| AC-07 happy path | an empty column is deleted without a confirmation | component | pressing delete removes the column, with no dialog |
| AC-08 error | a column name is bounded by the Text rule | unit | empty after trimming refused; 1 and 50 code points accepted; 51 refused |
| AC-08 error | an unusable column name is refused on add and on rename, and nothing is written | integration | refused with the 1–50 message in both cases; the board's columns are unchanged |
| AC-08 error | the column-name refusal keeps what was typed | component | the 1–50 message is shown in the editor, which still holds the typed name |
| AC-09 domain invariant | a column that holds a card cannot be deleted | unit | refused, naming the rule; the column and its cards are unchanged |
| AC-09 domain invariant | deleting a non-empty column is refused and the column and its cards are untouched | integration | refused with the non-empty message; every card is still in the store in the same column and position |
| AC-09 domain invariant | the non-empty refusal is shown under the column, and the delete button is not disabled in advance | component | the message appears under the column header and the cards are still drawn |
| AC-10 domain invariant | a board's last column cannot be deleted | unit | refused, naming the at-least-one-column rule |
| AC-10 domain invariant | deleting the only column of a board is refused | integration | refused with the last-column message; the board still has its column |
| AC-10 domain invariant | the last-column refusal is shown under the column | component | "A board must keep at least one column." under the header |
| AC-10b domain invariant (concurrent) | two members deleting the two remaining columns at once leave exactly one | integration | with contention forced on the board row, exactly one delete succeeds, the other is refused as last-column, and the board holds one column; repeated across the §6 randomised pairs |
| AC-11 domain invariant | a board's 21st column is refused | unit | the 20th is admitted and the 21st refused, naming the 20-column limit |
| AC-11 domain invariant | a board holding 20 columns refuses another | integration | refused with the 20-column message; nothing written |
| AC-11 domain invariant | simultaneous adds at 19 columns admit exactly one | integration | with contention forced, one add succeeds; the board ends with exactly 20 columns and 20 distinct positions |
| AC-11 domain invariant | the column-limit refusal is shown and the control stays | component | the 20-column message is shown in the editor and "Add column" is still offered |
| AC-12 happy path | an added card goes to the end of its column and counts toward the board | unit | the card takes the column's next position and both the column and board card counts rise by one |
| AC-12 happy path | an added card is stored last in its column and its title shown on the next open | integration | the next open lists the card's title last in that column |
| AC-12 happy path | adding a card shows its title last in the column and clears the form | component | the tile appears last and the form clears but stays open |
| AC-13 happy path | editing a card changes its text and its content version | unit | a changed title, a changed description, or both, are recorded and the content version moves once |
| AC-13 happy path | an edited card is seen with its new text by every member who next opens it | integration | a second member opens the card and sees the new title and description |
| AC-13 happy path | saving a card shows its new text | component | the card dialog and the tile show the saved text |
| AC-14 error | card text is bounded, and a description is never trimmed | unit | title: empty after trimming refused, 1 and 150 accepted, 151 refused; description: 10,000 code points accepted, 10,001 refused, and leading and trailing whitespace is kept exactly |
| AC-14 error | a card over any limit is refused whole, and nothing is written | integration | a valid title with an over-long description, and the reverse, are both refused, naming the limit that failed; neither field changes, on add and on edit |
| AC-14 error | the card refusal names the failed field and keeps both | component | the message sits under the field whose limit failed and both fields still hold what was typed |
| AC-15 domain invariant | a board's 1,001st card is refused | unit | the 1,000th is admitted and the 1,001st refused, naming the 1,000-card limit |
| AC-15 domain invariant | a board holding 1,000 cards refuses another in any column | integration | on a board seeded to 999 cards and then given one more, an add to each column is refused with the 1,000-card message; nothing written |
| AC-15 domain invariant | simultaneous card adds at 999 cards admit exactly one | integration | with contention forced, one add succeeds and the board ends with exactly 1,000 cards |
| AC-15 domain invariant | the card-limit refusal keeps what was typed | component | the 1,000-card message is shown and the title and description are still filled |
| AC-16 domain invariant | text with markup, a script or a web address is stored and returned exactly as typed | integration | the title and description come back character for character, with no escaping or stripping on the server; line breaks and repeated spaces are intact |
| AC-16 domain invariant | every piece of board text renders as literal characters | component | on the card tile, the card dialog, the board name, the column name and the kept-text notice: no element is created from the text, no script runs, a web address is not a link, and line breaks and repeated spaces are shown as typed |
| AC-17 error | the 121st change attempt within a rolling minute is refused and changes nothing | integration | with the clock under test control, 120 attempts go through (including ones naming a board the account is not a member of, and ones refused for other reasons); the 121st is refused with the retry time and nothing is written; the refused attempt does not count, and the account is admitted again once earlier attempts leave the minute |
| AC-17 error | the change-limit refusal is identical whichever board the change named | integration | once the account is at its limit, a change to its own board, to a board it is not a member of, and to one that never existed get refusals that are identical field for field |
| AC-17 error | an attempt with no active session does not count toward any limit | integration | unauthenticated attempts are refused as AC-28 says and leave the account's count where it was |
| AC-17 error | the change-limit refusal shows when to continue and keeps the typed text | component | "Changes are temporarily limited… Try again in N seconds." at the control, with the typed text still in place |
| AC-18 happy path | deleting a card keeps the others' relative order and frees its place in the count | unit | the column's other cards keep their relative order and both card counts fall by one |
| AC-18 happy path | a deleted card is afterwards refused exactly as a card that never existed | integration | an edit and a delete of the deleted card get refusals identical field for field to those for a random card id |
| AC-18 happy path | deleting a card asks for confirmation and removes its tile | component | the confirm dialog is shown; on confirm the tile is gone and the other tiles keep their order |
| AC-18b edge case | a change naming a since-deleted column or card is refused as never-existing and changes nothing | integration | each of: edit card, delete card, rename column, delete column, add card to column, gets the refusal a never-existing item gets; the board is unchanged |
| AC-18b edge case | a change to a vanished column or card keeps the typed text and says what vanished | component | after the board is read again, "That column no longer exists." or "That card no longer exists." is shown with the typed text selectable beside it |
| AC-19 happy path | the owner may rename the board, under the board-name bounds | unit | the rename is accepted for the owner and the Text rule applies |
| AC-19 happy path | a renamed board shows its new name in every member's list | integration | the owner's list and a fixture member's list both carry the new name |
| AC-19 happy path | renaming the board updates its title and the cached list | component | the header shows the new name, and the board list shows it with no new read |
| AC-20 happy path | the deletion confirmation must match the current name exactly after trimming | unit | "Q4 launch" and " Q4 launch " match; "q4 launch" and "Q4 launc" do not |
| AC-20 happy path | a confirmed deletion removes the board with all its columns and cards, and every later request about them is answered as never-existing | integration | no board, column, card or membership row remains; opening the board, and changing any of its former columns or cards, gets refusals identical field for field to those for random ids |
| AC-20 happy path | the deletion dialog enables Delete only on a matching name and returns to the list | component | Delete stays disabled until the typed name matches; on success the list is shown without the board |
| AC-20b error | a deletion confirmed with a mismatched name is refused and deletes nothing | integration | a wrong name, and the old name after a rename in another session, are both refused; the answer carries the current name; everything is still stored |
| AC-20b error | a mismatched deletion shows the board's current name | component | the refusal is shown in the dialog with the current name, and nothing is removed from the list |
| AC-21 happy path | a member who is not the owner makes every column and card change under the owner's rules | integration | as a fixture member: add, rename, move and delete a column, and add, edit and delete a card, are each accepted; the non-empty, last-column, limit and stale refusals apply to the member exactly as to the owner |
| AC-21 happy path | a member who is not the owner is offered every column and card control | component | the member view of the board draws the add, rename, move and delete controls for columns and cards |
| AC-22 authorization | a member who is not the owner may neither rename nor delete the board | unit | both are refused with the owner-only rule; the same calls from the owner are accepted |
| AC-22 authorization | a non-owner's rename or deletion of the board is refused and the board is unchanged | integration | as a fixture member, both are refused with "only the board owner may rename or delete a board", even with the correct confirmation name; name and contents unchanged |
| AC-22 authorization | a member who is not the owner is not offered rename or delete, and an owner-only refusal is still shown | component | the member header has no rename or delete board control; an owner-only answer that arrives anyway is shown as a refusal line |
| AC-23 domain invariant | saving or deleting a card against an outdated content version is refused | unit | refused with the card's current state, whether the title or the description was what changed since |
| AC-23 domain invariant | a card save or delete from an outdated view is refused and nothing changes | integration | another member, and separately the same account in a second session, edits the card first; the stale save and the stale delete are refused, the answer carries the current card, and the store holds the first edit |
| AC-23 domain invariant | a stale card save shows the card as it is now and keeps the typed text | component | the dialog shows the current title and description, and the member's text is offered to apply again |
| AC-24 domain invariant | a column move against an outdated layout version is refused | unit | refused with the current columns and order; adding, moving and deleting a column move the layout version, and renaming one does not |
| AC-24 domain invariant | a column move from an outdated view of the columns is refused | integration | after another session adds, moves or deletes a column, the stale move is refused and the answer carries the current columns and order; a rename in between does not make the move stale |
| AC-24 domain invariant | a stale move redraws the current order and says so | component | the columns are redrawn in the answer's order and "The columns changed since you last saw them." is shown in the notice region |
| AC-24b happy path | the three version counters move independently | unit | editing one card moves neither another card's version nor any column's; adding a card moves neither a column's name version nor the layout version |
| AC-24b happy path | a change to something other than what was changed since is accepted | integration | member A edits card 1; member B, without reopening the board, edits card 2, renames a column and adds a card, and all three are accepted |
| AC-25 authorization | for every read and change kind, a non-member's refusal is identical to the refusal for a board that does not exist | integration | for each read and change, sent to a real board the caller is not a member of and to a random id, with valid, invalid and stale bodies: status, headers and body are identical field for field, and nothing changes on any board |
| AC-25 authorization | no refusal to a non-member carries board content | integration | no response body, header or log line written for the attempt contains the board's name, column names or card text |
| AC-25 authorization | the board-not-available screen shows nothing of the board | component | SCR-08 carries no board name, id or content, and looks the same for a real board and an invented one |
| AC-26 authorization | a change naming another board's column or card is refused as if it did not exist | integration | for every change that names a column or card, using an id from a second board, both when the caller is a member of that board and when not: the refusal is identical to one for a random id, and neither board changes |
| AC-27 cross-context | without a session, a request about a real board is answered as one about a board that never existed | integration | for opening a board and for each change, the no-session answers for a real board and a random id are identical field for field |
| AC-27 cross-context | the sign-in form at a board address reveals nothing, and the return address is only followed inside the app | component | the form is identical to the ordinary sign-in form and no board read is made; after sign-in the return address is followed only when it is an in-app path (one leading slash, not two, no scheme), otherwise the board list is shown |
| AC-27 cross-context | a visitor following a board link signs in and lands back at that address | e2e-through-UI | a member is shown the board; a non-member and an invented id are shown the board-not-available screen; before sign-in, nothing on the page differs between the three |
| AC-28 cross-context | a change after the session ended changes nothing and is not attributed to the member | integration | after sign-out, and after expiry with the clock under test control, a change gets the session-not-recognised refusal, nothing is written, and the change counts toward no account's limit |
| AC-28 cross-context | typed text is kept for the same account and discarded, never shown, for another | component | the refusal sends the member to sign-in with the text kept in this browser; signing in as the same account offers "Apply again" with the text; signing in as a different account discards it unshown |

## Edge cases / error paths

- A name made only of whitespace: spaces, a tab, a non-breaking space (AC-02, AC-08, AC-14) → refused as empty. A description made only of whitespace → accepted and stored exactly as typed.
- A name of 100, a column of 50, a title of 150 code points, each made of emoji that take two UTF-16 units each (Text rule) → accepted, and stored whole in columns sized at twice the limit (data-model.md).
- Board creation at exactly 49 and 50 owned boards; column add at 19 and 20; card add at 999 and 1,000 (AC-03, AC-11, AC-15) → accepted, refused; accepted, refused; accepted, refused.
- A column move to an index outside 0…n-1 (AC-06) → refused after the stale check as a position not on this board (contracts/openapi.yaml), nothing moved; the client shows the generic "something went wrong" wording, because a correct client never sends it (screens.md, OQ-API-2/3).
- A request body that is not the expected shape, or a value of the wrong type, sent by a non-member (AC-25, ADR 0014 lenient binding) → the non-member refusal, never a validation answer. Membership is decided before the body is judged.
- A non-member's stale change: an outdated version on a board they do not belong to (AC-25) → the non-member refusal, never the stale-change answer, which would carry the current state.
- A column id from board B named on board A by someone who is a member of both (AC-26) → refused as never-existing. Membership of B does not make B's items valid on A.
- The board row stays contended through all three bounded retries (ADR 0015) → the busy refusal with its retry time, nothing written; never a partial write.
- A rename of a column in another session followed by a card add to that column (stale-change rule) → the add is accepted; adding is never stale.
- A deletion confirmed with the right name in the wrong case (AC-20) → refused as a mismatch.
- The 120th and 121st change attempts inside one rolling minute, and the 121st retried one second after the oldest attempt leaves the minute (AC-17) → accepted, refused; accepted.
- A return address of `//evil.example`, `https://evil.example/boards/x` or `javascript:…` after sign-in (AC-27) → ignored; the board list is shown.
- Kept text in the browser when a different account signs in (AC-28) → discarded without ever being rendered.
- The store unavailable while a board is opened → the failed block with Try again, never the board-not-available screen: an outage must not look like a board that does not exist.

## Test data

- Seed strategy: the builders `data-model.md` § *Test fixtures* names, in `tests/Uniqua.Projector.Api.IntegrationTests/Fixtures/BoardFixtures.cs` beside `AccountFixtures`. `ABoardAsync` goes through the API, so the owner, the three columns and the owned count are written by production code. `AMemberOfAsync` inserts a `Member` membership directly, which is the only way a non-owner member exists in this feature. `ABoardWithColumnsAsync` and `ABoardWithCardsAsync` seed up to one short of each ceiling, and `AnAccountOwningBoardsAsync` seeds 49 or 50 owned boards. These insert directly, keep every stored count consistent with the rows they add, and never go in a migration. Every address is under `example.test`.
- Domain data: unit tests build `Board`, `Column` and `Card` in memory through the aggregate's factory methods. They need no fixture.
- Component data: the stubbed transport answers with the problem documents and bodies of `contracts/openapi.yaml`, so a component test and the API agree on every problem code without a hand-written shape.
- Time: the rolling minute (AC-17) and session expiry (AC-28) are exercised by moving the injected test clock, never by waiting.
- Contention: the existing contention fixture is generalised from account rows to board, column and card updates. The one-short-of-the-ceiling pairs then actually collide on the board row instead of happening to run one after the other.
- Integration dependency: the throwaway database container the suite already shares, migrated from this feature's migrations. It is not a mocked store.
- Cleanup boundary: the container is per suite and isolation is per test. Every test creates its own accounts and boards and reads only what it created, so no row is shared between tests. This matters for more than tidiness: the change limit is kept in memory for the life of the suite, so a test that reused an account would inherit another test's attempts. For the same reason, each of the 1,000 race pairs and each of the 1,000 column sequences runs on a fresh board with fresh accounts. The e2e-through-UI tests register their own accounts and leave the container to be discarded with the suite.

## NFR validation (load)

- Opening a board holding 20 columns and 1,000 cards, p95 ≤ 300 ms → the §8 workload (sad.md §10): at least 25 accounts, each on its own board seeded by fixture to 20 columns and 1,000 cards, for 60 s. Board opens are interleaved with the change mix. Assert a server-side p95 of opens ≤ 300 ms.
- A single change, p95 ≤ 200 ms → the same run, with an even mix of add, rename, reorder, edit and delete across columns and cards. Assert a server-side p95 of accepted changes ≤ 200 ms. Stale refusals and limit refusals are excluded, because they measure a rule and not the path.
- Throughput ≥ 50 changes/s across boards → sustain 50 changes/s for 60 s on the reference machine, the 2-vCPU virtual machine the accounts-and-sessions spec §6 names. Assert that no change is refused by the per-account limit and that there is no error-rate regression. Use 30 accounts rather than the minimum 25, so each stays below 120 attempts per rolling minute while the total holds 50/s. At 25 accounts each would sit exactly on its limit, and the run would measure the limiter.
- In CI the same scenarios are a regression check only: they fail on a p95 more than 25% slower than the last recorded run, and the first runs set the baseline.
- Not load scenarios, although §6 gives them numbers. Each is an integration or unit row above:
  - The board invariants across 1,000 randomised pairs of simultaneous changes (AC-03, AC-10b, AC-11, AC-15) → an integration suite issuing the pairs concurrently, asserting 0 boards with no column, 0 non-empty columns deleted, 0 boards above 20 columns or 1,000 cards, and 0 accounts above 50 boards.
  - Column order consistency across 1,000 randomised sequences → an integration suite asserting 0 duplicated and 0 missing positions after every step.
  - Indistinguishable refusal → the AC-25, AC-26 and AC-27 comparison rows.
  - Per-account change rate → the AC-17 rows.
  - Content ceilings → the unit rows at each boundary.

## CI placement

- On every PR: unit and component, the fast suites. The CI workflow today runs the .NET tests and builds and lints the client but does not run the client's test script, so the component rows only guard anything once `implement` adds that step.
- On every PR: integration, including the AC-25/AC-26 comparison suite. The existing CI step already runs it against the container, and it carries the authorisation boundary, which is this feature's main risk.
- On every PR, as its own named job: the 1,000-pair race suite and the 1,000-sequence column-order suite. They are slow, but they are the only proof of the "0 boards…" and "0 duplicated positions" targets. If they push the PR run past a tolerable length, move them to merge on the main branch and keep a 50-pair smoke sample on PRs.
- On merge to the main branch and before a release: the two e2e-through-UI flows, because they need the client build served by the API and a real browser.
- On a schedule or before a release: load, on the reference machine. The same scenarios may run in CI as the 25% regression check only.
