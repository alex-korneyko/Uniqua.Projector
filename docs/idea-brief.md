---
status: Draft
owner: "Alex Korneiko"
updated_at: "2026-09-20"
depth: "medium"
---

# Idea brief — realtime-kanban

## 1. Raw idea

В качестве учебного проекта хочу создать приложение типа Trello. То-есть - создание проектов (доска Kanban), внутри проекта колонки с карточками задач, звдвчи можно делить на подзадачи. Естественно должны быть пользователи и возможность приглашения пользователя в проект по инвайту. Это приложение должно состоять как минимум из двух пректов: бекэнд (API) - .NET ASP.NET Core; фронтэнд - React

## 2. Problem

The owner has no deployed, publicly reachable full-stack artifact that a technical reviewer can both use and read, and a local-only project does not serve that purpose. The chosen vehicle is a Kanban board, which is a saturated category — a reviewer has seen many clones — so the project only pays off if it carries at least one decision worth discussing and a written record of why that decision was made. Success is defined concretely and externally: a stranger opens a public link, registers, creates a board, moves a card, and invites a second person, with no help from the owner.

## 3. Users

- **Reviewing engineer** (primary) — a tech lead or senior who clicks the link for about a minute to confirm it is alive, then spends fifteen minutes in the repository looking at layer boundaries, how authentication and access control are done, and whether tests exist and what they cover. This is the person the project is built for.
- **First-time visitor** (the demo proxy) — an unfamiliar person going through register → board → column → card → invite in a single session. The success criterion is written from their session, which is why unattended registration and a working invitation are load-bearing rather than optional.
- **Board owner and invited member** (the in-product roles) — the two accounts exercised side by side during the demo. The invited member is the account every access-control question is asked against: what they can see, what they cannot, and what happens when both move the same card.

## 4. Why now

There is no external trigger — no contract, no incident, no employer deadline. The trigger is the owner's own decision to convert a learning project into a deployable portfolio artifact, and the deadline is therefore self-imposed: 8–10 weeks of evenings and weekends, with the first production deploy fixed at week 2 and treated as non-negotiable. The budget moved during this interview: an initial 2–4 weeks was the estimate made before live collaborative updates entered the core scope, and 8–10 weeks is the figure that survived once that cost was named.

## 5. Out of scope

- **Subtasks as nested cards, or a tree of arbitrary depth** — a subtask is a checklist line on its card (text plus a done flag, with a counter on the card face); it has no assignee, no due date and no page of its own. Hierarchy would reach into every query, every screen and every access-control check, for roughly a week of the schedule.
- **Emailed invitations in the first version** — an invitation is a link the board owner copies and passes on themselves. Mail infrastructure costs days (service, verified domain, deliverability) and its failure mode lands live during the demo. Declared as a second stage, not a silent omission — tracked in §8.
- **Dependency links between cards** (blocks / depends on) — that answers a different question, the ordering of work rather than the decomposition of it.
- **Everything the reference product grew later** — comments, attachments, labels, due dates, activity history, and a phone layout. None of it is visible in a five-minute look or interesting in a fifteen-minute read.
- **Real use by a real team** — the audience is a reviewer, not a user base, so there are no backup, password-recovery or uptime commitments.
- **Conditionally: nothing further is cut if the schedule slips.** The owner's explicit call is that a slip is absorbed by tests, error handling and empty/loading states rather than by dropping a feature. This is recorded as the top risk in §6 rather than as a safe plan.

## 6. Risks

- **The declared slip-absorber points at exactly what the primary user inspects.** The plan is to keep every feature and let tests, error handling and empty/loading states take the hit — while the audience chosen in §3 is an engineer who opens the repository. A project that deploys on time and reads as rushed fails the review it was built to pass. This assumes the schedule holds; it becomes false the first week something takes twice as long, which is the normal case.
- **Stamina, not arithmetic, is now the binding constraint.** Extending to 8–10 weeks fixes the sum but moves the failure mode from "did not fit" to "burned out around week 5–6", when the novelty is gone and what remains is access control, invitation-token lifecycle and reconnection handling. The second failure mode has no cheap remedy.
- **The live-update channel has the longest tail of any commitment here.** Beyond sending messages it requires reconnection after a dropped connection, authorization on the channel itself so a non-member cannot subscribe to someone else's board, and defined behaviour on a host that suspends an idle application. This assumes a cheap host will hold a long-lived connection open; false on hosts that sleep idle instances.
- **Concurrent edits need an answer whether or not updates are live.** Two members moving the same card at the same moment is the first correctness question a reviewing engineer asks, and with no explicit rule the later write silently wins and someone's change disappears.
- **An invitation link is forwardable by nature.** Without a lifetime, single use and revocation it is a permanent open door into a board, and it is the cheapest thing for a reviewer to probe.
- **Category saturation may swallow the effort.** This assumes the live updates plus the written reasoning carry the differentiation; it is false if the repository ships without a document stating each decision and its trade-off, in which case the project reads as one more clone regardless of what is inside it.

## 7. Recommendation

Build the described product in full over 8–10 weeks, with the first production deploy fixed at week 2: a thin path from storage to browser (register → board → column → card) goes live first, and everything after that lands on top of a running deployment rather than ahead of one. The subtask is a checklist line, the invitation is a link with a lifetime, single use and revocation, and collaborative updates are a genuine server-push channel — that channel plus the written record of why each call was made is where the differentiation of this project actually lives. Because the owner has chosen to absorb any schedule slip in tests and finish rather than in features, the counterweight has to be built in from the start rather than bolted on at the end: a small, honest set of tests around access control and the card-move path, written as those features land. That is the one part of the plan worth revisiting at the week-4 checkpoint.

## 8. Open questions

- Emailed invitations — confirmed as a declared second stage rather than an open-ended "later". Owner: Alex Korneiko; due: after the first production deploy (week 2 onward).
- The resolution rule when two members move the same card at once is undecided. Owner: Alex Korneiko; due: before the card-move path is implemented.
- The hosting target for the API, the store and the front end, and whether it keeps a long-lived connection alive rather than suspending an idle instance. Owner: Alex Korneiko; due: before week 2, since the deploy date is fixed there.
- Whether a board lives inside a project (two levels of membership to check) or a project is a thin grouping over boards (one level) — this changes every access-control check. Owner: Alex Korneiko; due: before design.
- The quality floor the slip-absorber may not go below — the minimum testing that survives even when the schedule is behind. Owner: Alex Korneiko; due: week-4 checkpoint.
