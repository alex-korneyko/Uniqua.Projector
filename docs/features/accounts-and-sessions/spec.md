---
status: Draft
owner: "Alex Korneiko"
reviewers: ["Tech Lead", "Security Lead"]
updated_at: "2026-09-21"
feature_size: "M"
---

# Spec — accounts-and-sessions

> **Glossary:** [CONTEXT](../../../CONTEXT.md)
> **Reference module / docs / channels used:** `docs/idea-brief.md`, `docs/roadmap.md`, `docs/architecture-map.md`, `docs/adr/0003-authenticate-with-identity-and-an-httponly-cookie.md` — no reference module exists; the repository holds no source at this commit.

## 1. Context

A reviewing engineer who opens the public link has no way in, and a board has no one to belong to, until a stranger can create an account unaided. The primary user is the reviewing engineer described in `idea-brief.md` §3 — someone who spends about a minute on the live link and then fifteen minutes in the repository, where the first thing they look at is how authentication and access control are done. The secondary user is the first-time visitor, who must get from the public link to a signed-in state with no help from the owner; every later step of the product — a board, its members, an invitation — resolves against the identity this feature creates.

The trigger is self-imposed and structural rather than external: the first public deployment is fixed at week 2 and ships a thin path from storage to browser, and every step in the roadmap's dependency graph after the skeleton passes through this one. There is no account store to bolt identity onto later — a board created before accounts exist has no owner to attach to, so this is the earliest point at which the product can be built at all.

The committed approach is to make every rule governing a session **both stated in the repository and checkable from outside it**, so that a reader can hold the written promise against the observed behaviour instead of taking either on trust. Four rules are fixed here — how a session is carried, how long it lives, what ends it, and what happens when someone guesses at a password — and each is written down where the fifteen-minute read will find it and is exercisable on the live link. Competitive research found that no product in this category publishes any of these: one major collaboration product exposes session duration only as a buried administrator setting, and the leading board products document neither their session lifetime nor their post-registration behaviour, which is why a reader cannot learn them by clicking and must be handed them. This is the same wedge `idea-brief.md` §7 names — the written record of why each call was made — applied to the one area the primary reader probes first. The adversarial pass sharpened one commitment in particular: a persistent connection authorises its cookie only when it is opened, so "signing out revokes access everywhere" is a promise that must be built rather than inherited, and it is stated here as a promise precisely so that it cannot quietly not happen.

Traceability. Sources read: `docs/idea-brief.md` §2, §3, §5, §6, §7; `docs/roadmap.md` steps 2, 4, 7, 8 and open decisions D1–D5; `docs/architecture-map.md` §Constraints; `docs/adr/0003`. Two decisions moved during this pass on the strength of outside evidence: a temporary account lock after repeated failures was replaced by a progressive per-account delay, because OWASP's *Blocking Brute Force Attacks* documents account lockout as a denial-of-service vector against a named person and as a username-harvesting oracle — this diverges from a consequence recorded in ADR 0003 and is tracked in §8; and the cost of skipping address verification was fixed against GitHub's published restrictions on unverified addresses, which block creating, joining and being invited to anything.

## 2. Goals

- A stranger reaches a signed-in state from the public link without any intervention from the owner, in one session.
- The rules governing a session — how long it lives, what ends it, and what happens when someone guesses at a password — are each stated in the repository and observable from outside the application.
- Every later feature has exactly one identity to resolve against, so board membership, invitations and channel authorisation never invent a second notion of who someone is.

## 3. Non-goals

- **Password recovery and address verification** — out of scope for the product, not merely deferred: `idea-brief.md` §5 and the roadmap's own out-of-scope list both state that this project makes no password-recovery commitment, because the audience is a reviewer rather than a user base. Both would additionally require sending mail, which does not exist and is not planned for this stage.
- **Account removal and profile editing** — the same reasoning; neither is visible in a five-minute look or interesting in a fifteen-minute read.
- **Third-party or social sign-in** — ADR 0003 rejected delegating identity because it would remove the part of the project the owner set out to learn.
- **Roles or permissions inside an account** — membership is one level and belongs to a board, not to the account; inventing an account-level role here would create a second authorisation model for the board feature to contradict.

## 4. User stories

### US-01: Register unaided

**As a** visitor
**I want** to create an account from the public link with no help from anyone
**So that** I can reach the product on my own

### US-02: Sign in on return

**As a** visitor who already owns an account
**I want** to sign in with the address and password I chose
**So that** I get back to what is mine

### US-03: Stay signed in across days

**As an** account
**I want** my session to survive closing the browser
**So that** returning to the link days later does not cost me a sign-in

### US-04: End my session deliberately

**As an** account
**I want** signing out to end my access everywhere at once
**So that** leaving a shared machine does not leave the product open behind me

### US-05: Be seen as a person

**As an** account
**I want** to choose the name other members will see
**So that** my actions are attributed to a person rather than to an email address

### US-06: Be protected from password guessing

**As an** account
**I want** repeated wrong-password attempts against me to become progressively futile
**So that** a public sign-in form is not an open invitation

### US-07: Keep what I create

**As an** account
**I want** everything I create to stay bound to me
**So that** ownership never becomes ambiguous once boards and invitations exist

<!-- Note: `board owner`, `board member` and `invited member` are in the glossary but carry no user story here. They are project-wide roles that this feature creates the prerequisite for and that the board feature exercises. -->

## 5. Acceptance criteria

### AC-01 (US-01) — happy path

**Given** a visitor with no account, on the public link
**When** they submit an unused email address, a password of at least 8 characters, and a display name
**Then** the system creates the account, opens a session immediately without asking them to sign in again, and shows them their own display name

### AC-02 (US-01) — error

**Given** a visitor filling in the registration form
**When** they submit a password shorter than 8 characters
**Then** the system refuses to create the account and tells them, in plain language, that a password must be at least 8 characters long, leaving everything else they typed in place

### AC-03 (US-01) — domain invariant

**Given** an account already exists for a given email address
**When** a visitor tries to register with that same address
**Then** the system refuses and states plainly that the address is already registered, because an email address identifies exactly one account

### AC-04 (US-02) — happy path

**Given** a visitor who owns an account and has no active session
**When** they submit the address and password they registered with
**Then** the system opens a session and shows them their own display name

### AC-05 (US-02) — error

**Given** a visitor who owns an account
**When** they submit their address with the wrong password
**Then** the system refuses and says only that the address or the password is incorrect, without revealing which of the two was wrong

### AC-06 (US-03) — happy path

**Given** an account with an active session
**When** they close the browser entirely and return to the link the next day
**Then** the system still recognises them and shows them their own display name, without asking them to sign in

### AC-07 (US-03) — domain invariant

**Given** an account whose session has seen no activity for 14 days
**When** they return to the link
**Then** the system no longer recognises them and presents the sign-in form, because a session ends after 14 days of inactivity

### AC-08 (US-04) — happy path

**Given** an account with an active session
**When** they sign out
**Then** the system ends the session and presents them the view a visitor sees

### AC-09 (US-04) — cross-context

**Given** an account that is signed in and is holding an open live-update connection to a board
**When** they sign out
**Then** the system stops delivering board updates over that already-open connection, because the end of a session withdraws access over every channel at once and not only over the one the sign-out travelled on

### AC-10 (US-04) — authorization

**Given** a visitor whose session has ended, by signing out or by expiry
**When** they attempt to reach anything reserved for a signed-in account
**Then** the system refuses and presents the sign-in form, regardless of what their browser still holds

### AC-11 (US-05) — happy path

**Given** a visitor registering an account
**When** they supply a display name
**Then** the system records it and uses it, never the email address, wherever that account's actions are shown to anyone else

### AC-12 (US-06) — error

**Given** an account against which 5 consecutive sign-in attempts have already failed
**When** a further attempt is made with a wrong password
**Then** the system refuses it after a delay that grows with each additional failure, while a correct password is still accepted immediately, so that guessing becomes futile without the account ever becoming unusable to its owner

### AC-13 (US-07) — cross-context

**Given** a signed-in account creating any object elsewhere in the product
**When** the object comes into existence
**Then** the system records that account as its owner, and no later operation may leave that object without an owner

> **Verification timing.** AC-09 and the §6 row "Session survival across a redeploy" cannot be exercised while this feature closes: the live-update channel arrives at roadmap step 8 and the first real deployment at step 4. Both are binding commitments stated here and verified at those steps; `plan-tests` records them as deferred rather than as covered.

## 6. Non-functional requirements

| Aspect | Target | Measurement |
|---|---|---|
| Latency p95, sign-in | ≤ 600 ms | server-side timing on the sign-in path, sampled in the smoke run |
| Latency p95, registration | ≤ 800 ms | server-side timing, sampled in the smoke run |
| Latency p95, recognising a session on an ordinary read | ≤ 30 ms | server-side timing, sampled in the smoke run |
| Throughput | ≥ 20 sign-ins/s per instance | smoke test in CI |
| Password verification cost | ≥ 100 ms per attempt | deliberately slow; guarded by a unit test over the hashing parameters |
| Progressive delay under guessing | 6th consecutive failure delayed ≥ 2 s; 10th ≥ 30 s; a correct password never delayed | integration test |
| Session expiry accuracy | session ends within 14 days plus at most 1 hour after the last activity | integration test against a controllable clock |
| Session survival across a redeploy | 100% of unexpired sessions survive a redeploy of the instance | post-deployment check; first verifiable at roadmap step 4, when a real deployment exists |

<!-- N/A: Availability — `idea-brief.md` §5 and the roadmap's out-of-scope list both refuse any uptime commitment, since the audience is a reviewer rather than a team in production. Stating a target here would promise something nobody intends to measure or hold. -->

## 6.1 Security / privacy

- **Data classification:** confidential — the feature stores credentials and the real email addresses of real people who were invited to look at the project.
- **Personal data touched:** email address (contact identifier; medium sensitivity; retained indefinitely, since §3 rules out an account-removal path); password (never stored in a readable form); display name (low sensitivity; deliberately visible to other board members).
- **AuthZ/AuthN impact:** this feature introduces the product's only authentication boundary — every later capability check resolves against the account established here. No authorisation roles are added; membership remains a property of a board.
- **Abuse cases:**
  - *Password guessing*: progressive per-account delay (AC-12). Accepted residual risk — an attempt distributed thinly across many accounts is not slowed by a per-account counter.
  - *Account enumeration through the registration form*: accepted and deliberate. The form states plainly that an address is already registered (AC-03), because an ambiguous form defeats unattended registration, which is load-bearing. The sign-in form does not add a second oracle (AC-05).
  - *Address squatting*: anyone may claim an address they do not control, including the owner's, and there is no verification and no recovery. Accepted for this stage; mitigated operationally by claiming the owner's and the demo addresses at first deployment.
  - *Spam account creation*: rate limit — no more than 5 registrations per minute per request source.
  - *Session theft*: the session cookie is unreadable by page scripts; the 14-day window bounds exposure; ending a session withdraws access over every channel at once (AC-09, AC-10).
- **Security review:** Required — M-sized, introduces the product's only authentication boundary and its first personal data.

## 7. Metrics / KPIs

- **Unattended registration completion** — baseline: 0 (no registration exists), target: every observed first-time-visitor session reaches a signed-in state with no intervention from the owner, within the first week the public link is live.
- **Returning sign-in succeeds on the first attempt** — baseline: 0 (the path does not exist), target: ≥ 90% over the first month. This metric exists because opening a session at registration means the sign-in path is never exercised during the build.
- **Sessions surviving a redeploy** — baseline: 0 (unmeasured; the default behaviour is expected to be 0), target: 100%, checked after every deployment from week 2.
- **A reviewer can answer "how is the session carried, and what ends it" from the repository alone** — baseline: not answerable, nothing written; target: ≤ 2 minutes. Measurement plan: ask the first reviewer to try, and time it. This is the committed approach's own success measure.

## 8. Open questions

- [ ] How does signing out reach an already-open live-update connection, given that such a connection authorises its cookie only when it is opened? Default now: AC-09 states the required outcome without prescribing a mechanism. — owner: Alex Korneiko, due: before roadmap step 8
- [ ] Where does the material protecting the session cookie live, so that a redeploy of the self-hosted instance does not silently end every session? Default now: none — the default regenerates it per instance, which would make the 14-day window untrue in practice. — owner: Alex Korneiko, due: before roadmap step 4
- [ ] Does traffic on a live-update connection count as activity for the sliding window? Default now: it does not — only a deliberate action renews a session. — owner: Alex Korneiko, due: before roadmap step 8
- [ ] What normalisation applies to an email address at registration versus at sign-in (letter case, surrounding whitespace)? Default now: both are normalised identically before comparison. — owner: Alex Korneiko, due: before `/sdd:data-model`
- [ ] ADR 0003 records "lockout after repeated attempts" among the positive consequences of its chosen authentication mechanism, but AC-12 and §6 commit to a progressive delay instead, and no superseding decision is written down. Default now: the spec governs and the divergence is noted here. — owner: Alex Korneiko, due: before `/sdd:design`
