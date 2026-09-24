---
status: Draft
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-24"
feature_size: "M"
target_surfaces: []  # filled in §4 — subset of: backend-service | web-frontend | mobile-app | desktop-app | cli | worker | library-sdk. Read (never re-derived) by api/sequences/tasks/plan-tests/review → _shared/surfaces.md
---

# Software Architecture Document — boards-columns-cards

<!-- 12 Arc42 sections. Empty section → <!-- N/A: <one-line reason> -->. -->
<!-- C4 Context (L1) lives inline in §3. C4 Container (L2) lives inline in §5. -->
<!-- Numbers in §10 come VERBATIM from spec.md §6 NFR — no inventing, no rounding. -->

## 1. Introduction and goals

**Intent.** Give a signed-in account a board of its own — create it, shape its columns, capture work as cards — so that a first-time visitor reaches a board holding a card, unaided and in one session: the thin path the first public deployment ships (spec §2). The same feature introduces the product's **first authorisation boundary**. Every read and change is answered only after the board-membership check; an account that is not a member cannot tell a board it does not belong to from one that never existed; and the four board rules of spec §1 — one identical refusal for non-members, every named column and card belonging to the board that was checked, no deleting a non-empty or the last column, no change applied over a newer one — are enforced by the board itself and stated where a reviewer can find them. Every later roadmap step (card move, checklist, invitations, live updates) hangs off the board, column and card introduced here.

**Top-3 quality goals (1-liners; full scenarios in §10):**

1. **An indistinguishable membership boundary** — a non-member receives one refusal, field for field identical to the refusal for a board that does not exist, whatever else is wrong with the request; a column or card of another board is treated as nonexistent; no refusal ever carries board content.
2. **No silent loss under simultaneous changes** — the content ceilings and the column rules hold under races, and a change made from an outdated view is refused and explained rather than applied over a newer one.
3. **The thin path within the latency budget** — opening a full board and making a single change stay inside the spec §6 p95 targets on the reference machine.

When these conflict, they win in that order: the boundary first, because this feature is the first authorisation boundary the fifteen-minute read examines; latency is the goal that gives way.

**Stakeholders.**

| Role | Interest | Sign-off owner? |
|---|---|---|
| account | Creates boards; finds every board it is a member of, and only those | No |
| board member | Adds, renames, reorders and deletes columns; adds, edits and deletes cards | No |
| board owner | Additionally the only one who may rename or delete the board | No |
| visitor | Follows a board link to the sign-in form without learning anything about the board | No |
| reviewing engineer | Probes access control from outside with two accounts and reads how it is enforced; spec §1's primary user | No |
| Tech Lead | SAD approval | Yes |
| Security Lead | The authorisation boundary and the spec §6.1 abuse cases; a security review is required | Yes |

<!-- `reviewing engineer` is not a CONTEXT.md glossary role; it is kept because spec §1 names it the
     primary user, exactly as the accounts-and-sessions SAD did. Candidate for `/sdd:glossary`. -->

<!-- Decision overrides (¶4) — populated by the critic resolution loop, empty otherwise. -->

## 2. Constraints

**Technical** (from the code at `c5326d2`, not from the pre-scaffold map).
- C# on **.NET 10** (`net10.0`; ASP.NET Core, EF Core and Identity packages at **10.0.12**, pinned in `Directory.Packages.props`).
- **ASP.NET Core minimal APIs** — endpoints are route handlers such as `src/Uniqua.Projector.Api/Accounts/AccountEndpoints.cs`; there are no controllers.
- **Entity Framework Core 10 on SQL Server** — `AppDbContext` (an `IdentityDbContext`) in `src/Uniqua.Projector.Infrastructure/`; Fluent configurations applied from the assembly; four migrations so far.
- **Custom session authentication** — `SessionAuthenticationHandler` recognises the `projector_session` cookie (httpOnly, Secure, `SameSite=Lax`) against a server-side session record (ADR 0008).
- **Antiforgery on every state-changing request** — `src/Uniqua.Projector.Api/Antiforgery/AntiforgerySetup.cs`; the client sends `X-XSRF-TOKEN`.
- **Use cases are plain classes returning `Result<T, TError>`** (`src/Uniqua.Projector.Domain/Result.cs`) — no mediator library; ports include `IUnitOfWork` and `IClock` (`src/Uniqua.Projector.Application/Accounts/Ports/`).
- **Rate limiting is the repository's own in-memory `SlidingWindowLimiter`** driven by `IClock` (`src/Uniqua.Projector.Api/Accounts/SlidingWindowLimiter.cs`); the framework's built-in rate limiter is not used.
- **Client:** TypeScript 5, **React 19**, Vite, Tailwind, vendored shadcn/ui (`button`, `card`, `input`, `label` so far), **TanStack Query v5**, a `request<T>()` fetch wrapper with a problem-document `ApiError` (`src/Uniqua.Projector.Web/src/api/accounts.ts`). `@dnd-kit/core` and `@dnd-kit/sortable` are already installed. **There is no client-side router yet** — the app is one shell that swaps between the visitor and the signed-in view.
- **Test fixture:** `tests/Uniqua.Projector.Api.IntegrationTests/ApiFactory.cs` boots the app against a SQL Server container and already provides `TestClock`, `ContentionForcer` (a command interceptor that forces concurrency collisions deterministically), `CommandRecorder` and `LogRecorder`.
- **Four-project layering:** `Api → Application → Domain` and `Infrastructure → Application → Domain`; Domain references nothing; Api references Infrastructure only to register implementations.
- **One origin for client and API** (ADR 0003).

**Organisational.**
- One developer (the owner); no second in-team reviewer.
- The first public deployment (roadmap step 4) ships exactly this thin path and is fixed at **week 2**; this feature is the last thing blocking it.
- Sized **M**; route `standard`.
- A **security review is required** before it ships (spec §6.1) — the product's first authorisation boundary.

**Conventions.**
- `CLAUDE.md` and `docs/architecture-map.md` §Conventions: domain rules live in Domain; every failure is an RFC 9457 problem document from `src/Uniqua.Projector.Api/ProblemDetailsSetup.cs`; each layer registers itself through one `AddXxx(IServiceCollection)` in its own `DependencyInjection.cs`.
- **IDs:** `Uniqua.Projector.Domain.Ids.New()` — GUID version 7, generated by the application, never by the database.
- **Migrations:** one EF Core migration per schema change, generated from the model, reviewed as SQL.
- **ADR numbering is one sequence across the whole repository.** This feature's ADRs live in `docs/features/boards-columns-cards/adr/` and continue from **0012** (0001–0005 and 0011 in `docs/adr/`, 0006–0010 in accounts-and-sessions), so a bare «ADR 0003» keeps meaning exactly one document.
- **Licensing:** every dependency MIT, Apache-2.0 or BSD-style; the only recorded exception is the build-time `lightningcss` (ADR 0011).

**Regulatory / external.**
- **Data classification: confidential** — a board holds whatever its members type and is shown to no one else (spec §6.1).
- **No new personal data** — the only personal datum shown is the display name established by accounts-and-sessions.
- **Deletion is final** — no archive, trash or undo (spec §3); deleting a board removes its columns and cards.
- No formal compliance regime applies.

<!-- Divergence: docs/architecture-map.md is still the pre-scaffold target (reflects_commit 16bba53,
     hosting "undecided", target paths such as src/features/board/). This SAD follows the code and the
     self-hosted host the accounts-and-sessions SAD already assumed; carried as a §11 row. -->

## 3. Context and scope

<!-- 🎯 Why: draws the SYSTEM BOUNDARY — who talks to it from outside, where the trust zone ends.
     Without §3, §5 and §8 (authorization) blur — unclear what's «inside» vs «outside».
     📋 Write: 2–3 sentences of business context + an external-systems table + a C4Context block.
     📌 «External: none (deliberate, no third-party in v1)» is itself a decision worth stating.
     Trust boundary — the line past which you don't trust data without checking it.
     Never N/A — greenfield still draws the planned actors + external systems. -->

<Business context in 2–3 sentences. What the system does for whom.>

<!-- brownfield: <one-line scan summary> (or «N/A — greenfield repo» if no source existed) -->

**External systems (in / out):**

| Actor or system | Type | Interaction |
|---|---|---|
| <author role> | Person | <what they do> |
| <external service> | System (internal/external) | <interaction> |
| <identity provider> | System (external) | <provides auth tokens> |

**C4 Context (L1):** <!-- syntax → references/c4-mermaid-syntax.md. Real names, no <placeholder> stubs. -->

```mermaid
C4Context
    title <feature> — System Context

    Person(actor, "<Actor role>", "<intent>")
    System(app, "<Our system>", "<one-sentence description>")
    System_Ext(ext, "<External system>", "<one-sentence description>")

    Rel(actor, app, "<interaction>", "<protocol>")
    Rel(app, ext, "<interaction>", "<protocol>")
```

## 4. Solution strategy

<!-- 🎯 Why: the 3–4 STRATEGIC PILLARS every ADR grows from. Without §4 each ADR looks random —
     there's no umbrella. ⭐ The densest section — the blast-radius gate fires almost always here
     (decisions are irreversible + multi-module).
     📋 Write: 3–4 choices; each a heading + 2–3 sentences of rationale.
     📌 «Store content as a table of typed blocks» is a pillar — ADR-0001 grows from it. -->

**Top strategic choices (the seeds for ADRs):**

1. **<e.g. Module isolation through events>** — <2–3 sentences citing quality goals + constraints>.
2. **<e.g. Single-store persistence>** — <2–3 sentences>.
3. **<e.g. Server-rendered read side>** — <2–3 sentences>.

Each tactical decision in later sections should trace to one of these seeds. Tactical decisions that *contradict* a strategic choice are red flags — surface them in §11.

## 5. Building block view

<!-- 🎯 Why: INTERNAL DECOMPOSITION — modules, containers, datastores. The static topology: who
     may talk to whom. Without §5, §6 (the flows) has no vocabulary of participants.
     📋 Write: 1 ¶ on the style (layered / hexagonal / clean / event-driven) + a folder tree + a
     C4Container block.
     📌 Draw ONE Container per declared `target_surface` (frontmatter): a fullstack
     [backend-service, web-frontend] = a backend-API container + a web/SPA container; a
     [backend-service, mobile-app] = the API + the mobile app. The Container(web, …) line below is
     just one surface's container — swap/add per what was declared in §4. → _shared/surfaces.md
     📌 e.g. «web app, content API, media worker, datastore, object store, CDN». -->

<One paragraph: layered / hexagonal / clean / event-driven, and why.>

**Internal decomposition:**

```
<e.g. modules/<feature>/>
├── domain/       <entities + sentinel errors>
├── app/          <use cases / services>
├── infra/        <repository + integration impl>
├── ports/        <handlers, DTOs, error mapping>
└── wiring        <self-wiring entry point>
```

**C4 Container (L2):** <!-- syntax → references/c4-mermaid-syntax.md. Real names, no <placeholder> stubs. ONE Container per declared target_surface (frontmatter); the web container below is one example surface. -->

```mermaid
C4Container
    title <feature> — Containers

    Person(actor, "<Actor>")

    Container_Boundary(app, "<Our system>") {
        Container(web, "<Web/UI>", "<technology>", "<purpose>")
        Container(api, "<API/handler>", "<technology>", "<purpose>")
        ContainerDb(db, "<Datastore>", "<technology>", "<purpose>")
    }

    System_Ext(ext, "<External>", "<purpose>")

    Rel(actor, web, "<interaction>", "<protocol>")
    Rel(web, api, "<calls>")
    Rel(api, db, "<reads/writes>", "<driver>")
    Rel(api, ext, "<emits>", "<protocol>")
```

## 6. Runtime view

<!-- 🎯 Why: the RUNTIME FLOW of 1–2 critical scenarios — who talks to whom, when, in what order.
     Without §6, §5 is just boxes with no life.
     📋 Write: a Mermaid sequenceDiagram. Participants are names from §5 (don't invent new ones).
     Messages are semantic («saves a draft»), NO HTTP verbs / paths / status codes — endpoint-level
     sequences arrive at the `api` stage.
     📌 e.g. «author → web: composes draft → web → content API: save». Seed the primary flow(s) here;
     the `sequences` stage then covers every §5 AC (no cap). Never N/A for M+; XS/S keeps ≥1 happy-path flow. -->

**Critical flow 1: <flow name>**

```mermaid
sequenceDiagram
    actor Actor
    participant Web
    participant Service
    participant Store
    Actor->>Web: <action>
    Web->>Service: <call>
    Service->>Store: <write>
    Store-->>Service: ok
    Service-->>Web: result
    Web-->>Actor: confirmation
```

**Critical flow 2: <e.g. async event propagation>** — <if applicable, otherwise N/A>.

## 7. Deployment view

<!-- 🎯 Why: the TOPOLOGY DevOps must know without reading the deploy charts — how many replicas,
     where the background worker lives, AT WHAT NUMBERS we scale.
     📋 Write: 2–3 sentences on topology + monitoring + concrete threshold numbers.
     📌 e.g. «500 authors → partition by quarter» (not «we'll think about scale later»).
     🎯 N/A allowed for XS/S that reuses an existing deployment unit with no change.
     Deployment-diagram scaffold → templates/deployment.md. -->

<Topology in 2–3 sentences. Where it runs, replicas, scaling thresholds.>

**Monitoring:**
- <Metrics — e.g. `<metric_name>`>
- <Alerts — e.g. «worker lag > 10 min → page on-call»>
- <Tracing — e.g. spans on the request boundary>

**Scaling thresholds:**
- <e.g. comfortable in one table up to N rows/year>
- <e.g. partition by quarter above N rows/year>

<!-- For XS/S with no deployment change: <!-- N/A: reuses existing deployment unit, no infra change --> -->

## 8. Crosscutting concepts

<!-- 🎯 Why: CROSS-CUTTING PATTERNS spanning several modules: logging, errors, authorization, ID
     strategy, events, caching. ⭐ The second-densest section. A pattern inside one module is NOT
     here; a project-wide convention belongs in the convention file.
     📋 Write: a table — concept / convention / where defined. One row per concept.
     📌 e.g. «sortable time-based IDs generated in the app layer» as a default from the convention file. -->

| Concept | Convention | Where defined |
|---|---|---|
| Logging | <e.g. structured, fields `module=<name>`> | <convention file §X or here> |
| Authentication | <e.g. token-based via middleware> | <convention file §X> |
| Error handling | <e.g. domain sentinel → ports error mapping → JSON> | <convention file §X> |
| ID strategy | <e.g. sortable time-based ID in the app layer> | <convention file §X> |
| Internationalisation | <e.g. N/A, single language> | — |
| Observability | <e.g. tracing on the request boundary> | — |
| Events | <module-specific patterns, if any> | <here> |

## 9. Architecture decisions

<!-- 🎯 Why: the REVERSE INDEX onto the adr/ folder. `ls adr/` gives the files; §9 gives the
     semantics — why they exist, which SAD section they attach to, what status.
     📋 Write: a 4-column table, one row per ADR. Mixed status is fine.
     📌 e.g. «0001 | Store content as a table of typed blocks | Accepted | §4». -->

| # | Title | Status | Section |
|---|---|---|---|
| <NNNN> | <imperative — e.g. "Use a sliding-window counter for rate limiting"> | Accepted | §<N> |
| <NNNN> | <imperative — e.g. "Co-locate the worker in the API process"> | Accepted | §<N> |

ADR files live under `docs/features/<slug>/adr/NNNN-<title>.md`.

## 10. Quality requirements

<!-- 🎯 Why: the QUALITY TREE — take a goal from §1 and break it into concrete leaves: tests,
     metrics, configs, drills. ⭐ Without §10, §1 is a manifesto. With §10 each declaration maps
     to something PROVABLE.
     📋 Write: per §1 goal — When / Then / How-verify. Numbers from spec §6 NFR VERBATIM (don't
     round ≤250ms to ≤300ms — that's a critic F6 hit).
     📌 e.g. «p95 ≤ 500 ms on a block update, verified by a 100 req/s load test». -->

Each top-3 goal from §1 expanded into a full scenario:

**QG-1. <quality attribute>**
- **When:** <trigger condition>
- **Then:** <expected behaviour with numbers from spec §6 NFR>
- **How verify:** <test / chaos drill / load test / metric>

**QG-2. <quality attribute>**
- **When:** <trigger>
- **Then:** <expected>
- **How verify:** <how>

**QG-3. <quality attribute>**
- **When:** <trigger>
- **Then:** <expected>
- **How verify:** <how>

## 11. Risks and technical debt

<!-- 🎯 Why: ⭐ collects EVERYTHING that can break — not only the technical. Without §11 risks get
     discussed at standups and lost; debt lives only in the head of whoever accepted it.
     📋 Write: a risk/debt table — severity — mitigation — owner. Accepted debt in its own block.
     📌 The first risk is often a product risk, not a technical one. That's normal. -->

<!-- Severity literals: Low / Medium / High for regular risks; "Open question" for rows created by
     a Save-as-OQ resolution during the Socratic walk (see references/socratic.md). -->

| Risk / debt | Severity | Mitigation | Owner |
|---|---|---|---|
| <e.g. Worker lag may reach hours during a downstream outage> | Medium | <alert >10 min, on-call playbook, retry backoff> | <DevOps> |
| <e.g. No event-schema versioning in v1> | Medium | <ADR-NNNN planned for v2, tolerate unknown fields> | <Backend> |
| Open architectural decision: <decision-headline> | Open question | Resolve before <stage trigger or YYYY-MM-DD>; <inline rationale from the Save-as-OQ> | <owner> |

**Accepted debt (acceptable in v1, plan to fix later):**
- <e.g. the entity is immutable / unversioned — OK for v1, may need audit versioning in v2>

## 12. Glossary

<!-- 🎯 Why: ⭐ the DOMAIN GLOSSARY that ends arguments a year later («checkpoint — weekly or
     biweekly? quarter — calendar or fiscal?»).
     📋 Write: a term / meaning table. Business + technical terms mixed.
     📌 e.g. «Lesson | a unit inside a course made of blocks (text, video)». -->

| Term | Meaning |
|---|---|
| <e.g. domain object A> | <its meaning in this domain> |
| <e.g. domain object B> | <its meaning> |
| <e.g. domain invariant name> | <the rule, in plain language> |
