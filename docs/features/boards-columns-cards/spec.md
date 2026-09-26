---
status: Draft
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-24"
feature_size: "M"
---

# Spec — boards-columns-cards

> **Glossary:** [CONTEXT](../../../CONTEXT.md)
> **Reference module / docs / channels used:** `docs/idea-brief.md`, `docs/roadmap.md`, `docs/architecture-map.md`, `docs/session-rules.md`, `docs/features/accounts-and-sessions/spec.md` — no reference module was read; the closest precedent, accounts-and-sessions, is consulted by `design`.

## 1. Context

A signed-in account currently lands on nothing: there is no board to create, so the thin path the whole project is judged by — register → board → column → card — stops one step after it starts. The primary user is the reviewing engineer of `idea-brief.md` §3, who after a minute on the live link spends fifteen minutes in the repository and asks access-control questions first: what a member can see, what a non-member can learn, and what happens when two people change the same thing. The secondary user is the first-time visitor, who must get from a fresh account to a board holding a card with no help from the owner. Every later step of the roadmap — the card move, the checklist, invitations, live updates — hangs off the board, column and card this feature introduces.

The trigger is structural and self-imposed. The first public deployment (roadmap step 4) ships exactly this thin path and is fixed at week 2, so this feature is the last thing standing between the account work already shipped and a link a stranger can use. It is also the last point at which the membership rule can be made real before anyone other than the owner can join a board: invitations arrive at step 7, and a check that only ever sees one member per board is easy to get subtly wrong and hard to notice.

The committed approach is a board that **enforces its own rules and states each of them where a reader can check it from outside**. Four rules are fixed here: a non-member receives one identical refusal for any board they do not belong to, whatever else is wrong with their request, and that refusal never carries board content; every column and card a request names must belong to the board whose membership was checked; a column that still holds cards cannot be deleted, and a board always keeps at least one column; and a change made against an outdated view is refused rather than silently overwriting a newer change. Competitive research found that the category's best-known board product silently overwrites concurrent edits — the last save wins with no warning — and allows the last list on a board to be removed, while two other major board products document neither behaviour at all; an explicit "this changed since you last saw it" refusal and a hard block on deleting a non-empty column are therefore a citable improvement rather than a copied convention. The adversarial pass named cross-board id substitution — a member of one board naming a column or card that belongs to another — as the sharpest failure, and showed that the stale-change refusal, which returns the current state, would hand a stranger the whole board if it were ever answered before membership; both are guarded by their own acceptance criteria below. The measure of success is the deep-dive's: a stranger reaches a board holding a card unaided, and a reviewer can find each of the four rules in the repository and watch it hold on the live link.

Traceability. Sources read: `docs/idea-brief.md` §2, §3, §5, §6, §7; `docs/roadmap.md` steps 3–8 and open decisions D1, D2, D5; `docs/architecture-map.md` §Conventions ("domain rules live in Domain"); `docs/session-rules.md` §3 (identical refusals, so nothing is learned from the difference); `docs/features/accounts-and-sessions/spec.md` §6.1 (registration rate limit). Competitive sources, accessed 2026-09-24: Trello permissions, list-archive and API-limits documentation; Jira team-managed board column documentation (the same three default columns this spec adopts); GitHub Projects access documentation (deleting a project requires a higher permission than editing its items). Three decisions from the deep-dive shape the criteria: only the board owner may rename or delete the board; a non-empty column is blocked rather than deleted with its cards; and a new board starts with three columns. The stale-change rule decided here is the first concurrency rule in the product and is expected to set the precedent for roadmap decision D2 (two members moving one card), which remains open — see §8.

## 2. Goals

- A first-time visitor gets from a fresh account to a board holding at least one card, unaided and in one session — the thin path the first public deployment ships.
- Board membership is enforced on every read and every change, in a way a reviewer can probe from outside with two accounts and cannot tell apart from a board that does not exist.
- No change a member makes is lost silently: a board's structural rules hold under simultaneous changes, and a change based on an outdated view is refused and explained rather than applied over a newer change, whoever made it.

## 3. Non-goals

- **Moving a card within or between columns** — roadmap step 5 owns it together with open decision D2; in this feature a new card is placed at the end of its column and stays there.
- **Seeing other members' changes without reopening the board** — live updates are roadmap step 8; here a member sees others' changes the next time they open or refresh the board.
- **Adding members to a board** — invitations are roadmap step 7. Until then every board has exactly one member, its owner, but the membership check is the real one and is exercised against a member who is not the owner. In this feature such a member exists only through integration-test setup; no operation that adds a member ships, whether hidden, development-only or otherwise.
- **Projects grouping several boards** — membership is one level and belongs to a board (roadmap D5); a second level would reach into every access-control check this feature adds.
- **Archive, trash or undo for anything deleted** — `idea-brief.md` §5 makes no backup or recovery commitment; a deletion is final once confirmed.
- **Checklists, comments, labels, due dates, attachments, transferring ownership or leaving a board** — the checklist is roadmap step 6; the rest are out of scope for the project as a whole (`docs/roadmap.md` §Out of scope).

## 4. User stories

### US-01: Start a board

**As an** account
**I want** to create a board with a name and have it ready to use
**So that** I can start organising work without setting anything up first

### US-02: Find my boards

**As an** account
**I want** to see every board I am a member of, and only those
**So that** I can get back to my work and never stumble onto someone else's

### US-03: Shape the columns

**As a** board member
**I want** to add, rename, reorder and delete the columns of a board
**So that** the board reflects how the work actually flows

### US-04: Capture work as cards

**As a** board member
**I want** to add a card with a title and an optional description to a column, and edit both later
**So that** each piece of work is written down where everyone on the board can see it

### US-05: Remove a card

**As a** board member
**I want** to delete a card that is no longer needed
**So that** the board shows only work that is still real

### US-06: Rename and delete my board

**As a** board owner
**I want** to rename the board, and to delete it with everything on it once I confirm
**So that** the board's identity and existence stay under the control of the person who created it

### US-07: Work on a board I do not own

**As a** board member who is not the board owner
**I want** to change columns and cards exactly as the owner can, without being able to rename or delete the board
**So that** I can collaborate fully without being able to take the board away from its owner

### US-08: Not overwrite a newer change

**As a** board member
**I want** a change I make from an outdated view to be refused and explained rather than applied
**So that** no one's work disappears without anyone noticing

### US-09: Keep my board invisible to others

**As a** board owner
**I want** any account that is not a member to be unable to see, change, or even confirm the existence of my board
**So that** what I write on it is shared only with the people I chose

### US-10: Be asked to sign in first

**As a** visitor
**I want** a link to a board to send me to sign in without revealing anything about that board
**So that** a link that reaches the wrong person gives nothing away

## 5. Acceptance criteria

**Text rule** (applies to every name, title and description limit below, and to §6 Content ceilings). Board names, column names and card titles are stored without the whitespace at their start and end, and their length is counted after that trimming; whitespace means every character Unicode classes as whitespace, including tabs and non-breaking spaces. A card description is stored exactly as typed, with nothing trimmed. A character is one Unicode code point, so a typical emoji counts as one, and the form and the system count the same way.

### AC-01 (US-01) — happy path

**Given** an account signed in
**When** they create a board and give it a name of 1 to 100 characters
**Then** the system creates the board with that name, makes the account its board owner and only member, gives it three columns named To do, In progress and Done in that order, and opens it; a board name need not be unique, even among one account's boards

### AC-02 (US-01) — error

**Given** an account creating a board
**When** they submit a name that is empty once surrounding spaces are removed, or longer than 100 characters
**Then** the system refuses to create the board and tells them the name must be between 1 and 100 characters, leaving what they typed in place

### AC-03 (US-01) — domain invariant

**Given** an account that already owns 50 boards
**When** they try to create another
**Then** the system refuses and tells them an account can own at most 50 boards, because each account's share of the product is bounded — and the limit holds even when several boards are created at the same moment

### AC-04 (US-02) — happy path

**Given** an account that owns some boards and is a member of others
**When** they open their list of boards
**Then** the system lists every board they are a member of, most recently created first, marks the ones they own, and lists no other board

### AC-05 (US-03) — happy path

**Given** a board member on a board with fewer than 20 columns
**When** they add a column with a name of 1 to 50 characters
**Then** the system adds the column at the end of the board and shows it to them; a column name need not be unique on its board

### AC-06 (US-03) — happy path

**Given** a board member on a board with several columns
**When** they rename a column, or place a column at a different position among the others
**Then** the system records the new name or order, and every member who next opens the board sees the columns named and ordered that way

### AC-06b (US-08) — domain invariant

**Given** a board member who is viewing a column, and that same column since renamed — by another member, or by the same account in another tab or on another device
**When** the first member saves their own new name for it, or deletes it
**Then** the system refuses it, tells them the column was renamed since they last saw it, shows its current name, and keeps any name they typed so they can apply it again

### AC-07 (US-03) — happy path

**Given** a board member on a board with at least two columns, one of which holds no cards
**When** they delete that empty column
**Then** the system removes it and the remaining columns keep their relative order

### AC-08 (US-03) — error

**Given** a board member adding or renaming a column
**When** they submit a name that is empty once surrounding spaces are removed, or longer than 50 characters
**Then** the system refuses and tells them a column name must be between 1 and 50 characters, leaving what they typed in place

### AC-09 (US-03) — domain invariant

**Given** a board member on a board where a column still holds at least one card
**When** they try to delete that column
**Then** the system refuses, leaves the column and its cards untouched, and tells them a column that still holds cards cannot be deleted

### AC-10 (US-03) — domain invariant

**Given** a board member on a board that has exactly one column, holding no cards
**When** they try to delete it
**Then** the system refuses and tells them a board must keep at least one column

### AC-10b (US-03) — domain invariant (concurrent)

**Given** a board with exactly two columns, both empty, and two members each viewing it
**When** each deletes a different one of the two columns at the same moment
**Then** exactly one deletion succeeds, the other is refused because a board must keep at least one column, and the board is left with one column

### AC-11 (US-03) — domain invariant

**Given** a board member on a board that already has 20 columns
**When** they try to add another
**Then** the system refuses and tells them a board can hold at most 20 columns — and the limit holds even when several columns are added at the same moment

### AC-12 (US-04) — happy path

**Given** a board member on a board holding fewer than 1,000 cards
**When** they add a card to a column with a title of 1 to 150 characters and, optionally, a description of up to 10,000 characters
**Then** the system adds the card at the end of that column and shows its title on the board

### AC-13 (US-04) — happy path

**Given** a board member viewing a card
**When** they change its title, its description, or both, and save
**Then** the system records the change, and every member who next opens the card sees the new text

### AC-14 (US-04) — error

**Given** a board member adding or editing a card
**When** they submit a title that is empty once surrounding spaces are removed or longer than 150 characters, or a description longer than 10,000 characters
**Then** the system refuses the whole change, tells them which limit was exceeded, and leaves what they typed in place

### AC-15 (US-04) — domain invariant

**Given** a board member on a board that already holds 1,000 cards
**When** they try to add another card to any of its columns
**Then** the system refuses and tells them a board can hold at most 1,000 cards — and the limit holds even when several cards are added at the same moment

### AC-16 (US-04) — domain invariant

**Given** a board member who writes a card title or description containing markup, a script or other formatting syntax
**When** any member views that card
**Then** the system shows exactly the characters that were typed, as plain text, and nothing in them is ever run or rendered as formatting — line breaks and repeated spaces are shown as typed, and a web address stays plain text rather than becoming a link

### AC-17 (US-04) — error

**Given** an account that has attempted 120 changes within the past minute — every attempt to create, rename, reorder, edit or delete a board, column or card counts, whichever board it named, whether or not they belong to it, and whether it was accepted or refused for any reason other than this limit, a missing or wrong proof of origin, or a body that is not readable as a request at all
**When** they attempt a further change
**Then** the system refuses it, says plainly that changes are temporarily limited, tells them when they may continue, and changes nothing on any board — answering identically whichever board the change named, so the refusal reveals nothing about any board. An attempt refused by this limit does not itself count, so an account that stops is admitted again once its earlier attempts fall out of the minute; an attempt made with no active session (AC-28) belongs to no account and does not count. The two further exemptions are answered before the account's attempts are counted and identically for every board, so they reveal nothing about any board (sad.md §8 Rate limiting; amended at review 2026-09-26, B5)

### AC-18 (US-05) — happy path

**Given** a board member viewing a card
**When** they delete it and confirm
**Then** the system removes the card, the other cards in its column keep their relative order, and a member who then tries to change that card is refused exactly as for a card that never existed

### AC-18b (US-08) — edge case

**Given** a board member whose view still shows a column or card that has since been deleted
**When** they submit a change that names it — editing or deleting that card, renaming or deleting that column, or adding a card to that column
**Then** the system refuses it exactly as it refuses a column or card that never existed, changes nothing on the board, and keeps what they typed in place

### AC-19 (US-06) — happy path

**Given** the board owner viewing their board
**When** they rename it to a name of 1 to 100 characters
**Then** the system records the new name and every member sees it in their list of boards

### AC-20 (US-06) — happy path

**Given** the board owner viewing their board
**When** they choose to delete it and confirm by entering the board's current name exactly — the same letters in the same case, after the Text rule's trimming
**Then** the system itself checks that confirmation, deletes the board together with all its columns and cards, and from then on answers any request about that board, or anything that was on it, exactly as it answers for a board that never existed

### AC-20b (US-06) — error

**Given** the board owner deleting their board
**When** the name they enter does not match the board's current name — including because the board was renamed since they opened it
**Then** the system refuses, deletes nothing, and shows them the board's current name

### AC-21 (US-07) — happy path

**Given** a board member who is not its board owner
**When** they add, rename, reorder or delete columns, or add, edit or delete cards
**Then** the system accepts each change exactly as it would from the board owner, under the same rules

### AC-22 (US-07) — authorization

**Given** a board member who is not its board owner
**When** they try to rename the board or delete it
**Then** the system refuses, leaves the board unchanged, and tells them only the board owner may rename or delete a board

### AC-23 (US-08) — domain invariant

**Given** a board member who opened a card, and that same card's title or description since changed — by another member, or by the same account in another tab or on another device
**When** the first member saves their own change to that card, or deletes it
**Then** the system refuses it, tells them the card was changed since they opened it, shows them the card as it is now, and keeps any text they typed so they can apply it again

### AC-24 (US-08) — domain invariant

**Given** a board member whose view of a board's columns is outdated because a column has since been added, reordered or deleted — by another member, or by the same account in another tab or on another device; a rename alone does not change the order
**When** they place a column at a new position
**Then** the system refuses the reorder, tells them the columns changed since they last saw them, and shows the current columns and order

### AC-24b (US-08) — happy path

**Given** two board members viewing the same board
**When** one changes a card and, afterwards, the other changes a different card or a column, without having reopened the board
**Then** the system accepts both changes, because a change is refused as stale only when the very thing it changes was changed since that member last saw it, as the stale-change rule below defines

**Stale-change rule — what each change is checked against.** A change is refused as stale when, since the member last saw it:
- *editing or deleting a card* — that card's title or description was changed (a change to either counts, whichever the member is changing) (AC-23);
- *renaming or deleting a column* — that column was renamed (AC-06b);
- *placing a column at a new position* — a column of the board was added, placed elsewhere or deleted (AC-24);
- *adding a column or a card* — never; it is placed at the end and overwrites nothing.

Adding, editing or deleting a card is not a change to its column. The rule is the same whoever made the earlier change — another member, or the same account in another tab or on another device.

### AC-25 (US-09) — authorization

**Given** an account that is not a member of a board
**When** they try to open it, or submit any change to it or to anything on it — including a change that is itself invalid or based on an outdated view
**Then** the system gives them exactly the same refusal it gives for a board that does not exist, reveals nothing of the board's content, and changes nothing on any board

### AC-26 (US-09) — authorization

**Given** a board member of one board, and any other board — whether or not they are also a member of it
**When** they submit a change to the first board that names a column or a card belonging to the other board
**Then** the system refuses it exactly as if that column or card did not exist, and changes nothing on either board

### AC-27 (US-10) — cross-context

**Given** a visitor with no active session
**When** they open a link to a board
**Then** the system presents the sign-in form and reveals nothing about the board — not its name and not whether it exists — answering a link to a real board exactly as it answers one to a board that never existed; once they sign in, they are returned to that link's address and answered there as any signed-in account is — the board if they are a member, otherwise the refusal of AC-25

### AC-28 (US-10) — cross-context

**Given** a board member whose session has ended — they signed out on this browser, or it expired — while the board is still open in front of them
**When** they submit a change
**Then** the system changes nothing on the board, presents the sign-in form, and does not treat the change as coming from that member; what they typed is kept in this browser so that, once they sign in again as the same account, they can apply it again, and it is discarded, never shown, if a different account signs in there

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Latency p95, opening a board holding 20 columns and 1,000 cards | ≤ 300 ms | server-side timing, sampled in the smoke run on the reference machine (workload: §8) |
| Latency p95, a single change (add, rename, reorder, edit, delete) | ≤ 200 ms | server-side timing, sampled in the smoke run on the reference machine (workload: §8) |
| Throughput | ≥ 50 changes/s across boards | smoke test on the reference machine — the 2-vCPU virtual machine on the self-hosted host named in the accounts-and-sessions spec §6; in CI a regression check only (workload and tolerance: §8) |
| Board invariants under simultaneous changes | 0 boards left with no column, 0 non-empty columns deleted, 0 boards above 20 columns or 1,000 cards, and 0 accounts owning more than 50 boards, across 1,000 randomised pairs of simultaneous changes — column deletes, column adds, card adds and board creations, including pairs made one short of each ceiling | integration test issuing the pairs concurrently against the real store |
| Column order consistency | 0 duplicated and 0 missing positions across 1,000 randomised sequences of accepted reorders, adds and deletes — every column of a board holds exactly one distinct position | integration test over randomised sequences |
| Indistinguishable refusal | 100% of read and change kinds, 0 differences: for each, the refusal for a board the caller is not a member of is identical, field for field, to the refusal for a board that does not exist | integration test comparing the two responses directly |
| Per-account change rate | at most 120 change attempts per account per rolling minute, counted as AC-17 defines (attempts refused by this limit do not count); the 121st is refused and nothing changes on any board | integration test against a controllable clock |
| Content ceilings | 50 owned boards per account; 20 columns and 1,000 cards per board; board name ≤ 100, column name ≤ 50, card title ≤ 150, description ≤ 10,000 characters | unit tests on the domain rules at each boundary |

<!-- N/A: Availability — `idea-brief.md` §5 and the roadmap's out-of-scope list refuse any uptime commitment; the audience is a reviewer rather than a team in production. -->

## 6.1 Security / privacy

- **Data classification:** confidential — a board holds whatever its members type, and the product promises to show it to no one else.
- **Personal data touched:** none new. Board names, column names and card text are free text an account chooses to write; the only personal datum shown is the display name already established by accounts-and-sessions.
- **AuthZ/AuthN impact:** introduces the product's first authorisation boundary. Every read and change is answered only after checking that the signed-in account is a member of the board concerned, and before any other check but one: the per-account change limit (AC-17), which depends only on the account, counts every attempt whichever board it names, and refuses identically for any board, so it reveals nothing about one. The order is therefore: session, per-account limit, membership, then everything else; every column and card a request names is checked to belong to that same board. A refused attempt changes nothing on any board; operational logging of refused attempts is permitted as long as it carries no board content. One capability is reserved to the board owner — renaming and deleting the board — and must be shown to refuse a board member who is not the owner, even though no such member can join until invitations exist.
- **Abuse cases:**
  - *Cross-board substitution* — naming another board's column or card through one's own board: refused as though the item did not exist, nothing changed on either board (AC-26). The sharpest vector from the adversarial pass.
  - *Existence and content probing by a non-member* — including requests that are deliberately invalid or stale, to see whether validation or conflict answers are given before membership: one identical refusal for every board the caller does not belong to, and no refusal ever carries board content to a non-member (AC-25, AC-27).
  - *Stored markup* — a script or markup in a title or description, aimed at the invited member who will view it from step 7: always shown as literal text, never run or rendered (AC-16).
  - *Spam creation filling the store* — scripted accounts creating boards, columns and cards to exhaust the production store's size ceiling (roadmap D1): content ceilings per account and per board, text-length ceilings, and at most 120 changes per account per minute (AC-03, AC-11, AC-14, AC-15, AC-17), on top of the existing limit of 5 registrations per minute per request source. Accepted residual risk — many accounts registered from many request sources still add up; this is watched operationally through the store's size rather than prevented.
  - *Owner-only check posing as a membership check* — while every board has one member, a check that only compares the requester to the owner passes every test: the member-level and owner-level capabilities are each exercised against a board member who is not the owner (AC-21, AC-22).
  - *A change forged by another site* — every change continues to require the proof of origin established by accounts-and-sessions, since the session cookie is attached by the browser on its own.
- **Security review:** Required — M-sized and introduces the product's first authorisation boundary.

## 7. Metrics / KPIs

- **Unattended thin path** — baseline: 0 (no board exists), target: every first-time visitor the owner hands the public link to reaches a board holding at least one card with no intervention from him, within the first week after the first public deployment. Measurement plan: the owner asks each person he sends the link to, and counts the answers; no visitor-level tracking is added.
- **Access-control probes pass on the live deployment** — baseline: 0 (no probe exists), target: 100% of a scripted probe set refused as specified — a non-member reading and changing a real board, a non-member sending an invalid or stale change, and cross-board substitution on every deployment from week 2; a board member who is not the owner renaming or deleting the board on every deployment from the first one after roadmap step 7, since no such member can exist on the live site before invitations do (the rule itself is proved by the integration tests behind AC-21 and AC-22 from this feature onward).
- **A reviewer can find the board's four rules and the test that proves each** — baseline: not answerable, nothing written; target: ≤ 2 minutes from opening the repository. Measurement plan: ask the first reviewer to try, and time it. This is the committed approach's own success measure.

## 8. Open questions

- [ ] Does roadmap decision D2 (two members moving one card at once) inherit the stale-change rule fixed here in AC-23 and AC-24? Default now: yes — a move made against an outdated view is refused and the current state shown. — owner: Alex Korneiko, due: before `/sdd:specify` of roadmap step 5
- [x] Roadmap D5 asks for one-level membership to be recorded as an ADR before the board is specified, and none exists yet. Default now: the glossary's `board` entry and §3 state the rule, and the roadmap's D5 is amended to have `design` record the ADR. — owner: Alex Korneiko, due: during `/sdd:design boards-columns-cards` — **Resolved:** ADR 0013 (`adr/0013-grant-board-access-through-one-level-membership-records-with-an-owner-role.md`) records it; ticked at review 2026-09-26.
- [x] Where does a visitor land after signing in from a board link (AC-27) — on that board if they are a member, or on their list of boards? **Resolved 2026-09-24 in `ux-flows.md` (flow US-10):** back at the address they came from, answered by the membership rule — a member sees the board, anyone else the same refusal as for a board that does not exist, which is exactly what opening the link while signed in would show, so the return reveals nothing new. The same return applies after AC-28's sign-in. The return address is limited to addresses inside the application (design input). — owner: Alex Korneiko, due: before `/sdd:ux-flows boards-columns-cards`
- [x] What workload do the §6 latency and throughput smoke runs use — accounts, boards, mix of changes, duration, number of samples — and what slowdown fails the CI regression check? Default now: at least 25 accounts, each on its own board (the per-account limit caps one account at 2 changes/s), an even mix of the change kinds in §6, 60 s per run, and CI fails on a p95 more than 25% slower than the last recorded run. — owner: Alex Korneiko, due: during `/sdd:design boards-columns-cards` — **Resolved:** sad.md §10 fixes the smoke workload, implemented by `BoardLatencyBudgetTests`; ticked at review 2026-09-26.
- [ ] The security review §6.1 requires has not been recorded — no record exists under `_audit/`. Deferred at review 2026-09-26 (`_review/review-2026-09-26.md`, finding B8). Default now: the independent code review's boundary checks (AC-25, AC-26, order of checks) stand in until the Security Lead signs off. — owner: Security Lead, due: before `/sdd:ship boards-columns-cards`
- [ ] The seven new UI components (`Dialog`, `Textarea`, the `destructive` Button variant, `PlainText`, `InlineNameEditor`, `BoardColumn`, `KeptTextNotice`) are still "pending" in `screens.md` because `docs/design-system.md` does not exist. Deferred at review 2026-09-26 (finding Q4). Default now: `screens.md` "New components" is the inventory. — owner: Alex Korneiko, due: before `/sdd:screens` of the next UI feature, via `/sdd:design-system`
