---
status: Accepted
owner: "Alex Korneiko"
reviewers: []
updated_at: "2026-09-22"
feature_size: ""
ticket: "n/a — review 2026-09-22 R-29 (docs/features/accounts-and-sessions/_review/review-2026-09-22.md)"
---

# 0011 — Accept lightningcss (MPL-2.0) as a build-time licence exception

- **Status:** Accepted
- **Date:** 2026-09-22
- **Deciders:** Alex Korneiko (owner), Claude (facilitator)

## Context

The repository's licensing rule is permissive only: every dependency MIT, Apache-2.0 or BSD-style, with
copyleft flagged and a permissive or commercial alternative proposed. The independent review of
`accounts-and-sessions` found one dependency that does not meet it and is recorded nowhere:
**`lightningcss`, licensed MPL-2.0** — a weak, file-level copyleft.

It reaches the client through two paths, both development dependencies (`package-lock.json`, all entries
`dev: true`):

- `vite` 8.3.0 depends on `lightningcss` ^1.33.0 directly;
- `@tailwindcss/vite` → `@tailwindcss/node` depends on `lightningcss` 1.32.0.

Each version arrives with its per-platform native binaries (`lightningcss-<platform>`), also MPL-2.0 and
optional. It was already present when the skeleton was scaffolded on 2026-09-20, when the owner chose to
keep Tailwind v4 over downgrading to Tailwind v3 to avoid it; that choice was never written into the
repository.

## Decision drivers

- The rule exists to keep copyleft obligations out of the product this organization ships.
- MPL-2.0's obligations attach to the MPL-licensed files themselves, when those files are distributed.
- `lightningcss` is a CSS parser and minifier that runs on the build machine. What it produces — the CSS in
  `wwwroot/assets` — is output, not a copy of its source; no MPL-licensed file is bundled, linked or
  shipped.
- ADR 0005 chose Vite and Tailwind for the client. Vite 8 itself depends on `lightningcss`, so no Tailwind
  version avoids it.

## Considered options

1. **Keep the stack and record `lightningcss` as a build-time exception** (this record).
2. **Downgrade Tailwind to v3.** Removes one of the two paths, not the other: Vite 8 still brings it.
3. **Downgrade Vite as well, to a major without the `lightningcss` dependency.** Removes it, at the cost of
   running the build on an older, less supported toolchain for a licence obligation that does not reach
   the product.

## Decision outcome

**Chosen:** Option 1. Neither the scope of MPL-2.0 nor the way the tool is used brings its obligations into
anything shipped, and options 2 and 3 cost support and currency for no change in what is distributed.

The exception is exactly as wide as this: **MPL-2.0 (or similar file-level weak copyleft), reached only
through a build-time tool that this repository neither modifies nor ships.** It does not extend to GPL,
LGPL or AGPL code, which stays barred outright, nor to any copyleft code that would be linked into or
distributed as part of the product.

## Consequences

**Positive**
- The licence position is on record where a reader of the conventions will find it, instead of being
  rediscovered by each review.
- The client keeps its current, supported toolchain.

**Negative**
- The permissive-only rule now has one stated exception, and every exception invites the next. The scope
  above is deliberately narrow for that reason.

**Neutral**
- **Revisit if** `lightningcss` (or its output) would be bundled into the client or the API; if a file of
  it is ever modified or vendored into this repository; if the build machine starts distributing the tool
  itself (for example a published build image); or if its licence changes.

## Links

- ADR 0005 — the client's build tooling.
- `docs/architecture-map.md` § Stack, Licensing.
- `CLAUDE.md` § Licensing: permissive only.
