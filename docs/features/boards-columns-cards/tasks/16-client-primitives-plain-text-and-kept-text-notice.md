---
id: T16
title: "Vendor Dialog and Textarea, add the destructive Button variant, and build PlainText and KeptTextNotice"
layer: "ui"
deps: []
blocks: ["T19", "T20"]
acs: ["AC-16"]
files_hint:
  - "src/Uniqua.Projector.Web/src/components/ui/dialog.tsx"
  - "src/Uniqua.Projector.Web/src/components/ui/textarea.tsx"
  - "src/Uniqua.Projector.Web/src/components/ui/button-variants.ts"
  - "src/Uniqua.Projector.Web/src/features/boards/PlainText.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/KeptTextNotice.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/PlainText.test.tsx"
  - "src/Uniqua.Projector.Web/src/features/boards/__tests__/KeptTextNotice.test.tsx"
  - "src/Uniqua.Projector.Web/package.json"
owner: "Alex Korneiko"
estimate: "S"
context_budget: "M"
status: "todo"
---

# T16 — Vendor Dialog and Textarea, add the destructive Button variant, and build PlainText and KeptTextNotice

## Place in the sequence

- **Blocked by:** nothing — it starts in wave 1 · **Blocks:** T19 — Build the board list (SCR-02) and the create-board dialog (SCR-03), T20 — Build the board screen (SCR-04) with its owner header, card tiles and add-card form, and the not-available screen (SCR-08) · **Wave:** 1 — the client branch starts here, in parallel with the whole backend; the screens only need the contract, not a running API.
- **Lane:** shares `package.json` with T18 — serialized with it.

## Why (user story)

> **As a** board member
> **I want** to add a card with a title and an optional description to a column, and edit both later
> **So that** each piece of work is written down where everyone on the board can see it
>
> — `spec.md §4, US-04, verbatim` · full text: [spec.md](../spec.md)

This task builds the few primitives every board screen needs, including the one component that makes AC-16 true everywhere by construction.

## Inlined context

> | `Dialog` (shadcn/ui, vendored as source under `components/ui/dialog.tsx`; adds `@radix-ui/react-dialog`, MIT) | SCR-03, SCR-05/06 and SCR-07 open over the current screen. `Card` has no focus trap, no Esc handling and no `aria-modal` |
> | `Textarea` (shadcn/ui, vendored as `components/ui/textarea.tsx`; no dependency) | A card description is multi-line, up to 10,000 characters |
> | `Button` variant `destructive` (edited in place in `button-variants.ts`, using the existing `--color-destructive` token) | SCR-06 and SCR-07 confirm deletions that cannot be undone |
> | `PlainText` (feature) | This is the AC-16 rule in one place: React text nodes only, `whitespace-pre-wrap break-words`, never `dangerouslySetInnerHTML`, never linkified. sad.md §8 *Text rendering* wants it proved by one component test instead of at every call site |
> | `KeptTextNotice` (feature) | It keeps typed text visible when the control that held it is gone (AC-18b), or across a sign-in (AC-28). It is built from `Card` and `Button` |
>
> — `screens.md §New components, abridged` · full text: [screens.md](../screens.md)

> `KeptTextNotice` in the notice region: «You were signed out before this was saved:», the kept text as `PlainText`, `Button` «Apply again», `Button` (ghost) «Discard». […] kept-text gone: «That column no longer exists.» or «That card no longer exists.», any typed text as `PlainText` (it can be selected and copied), and `Button` (ghost) «Dismiss».
>
> — `screens.md §SCR-04, kept-text offer + kept-text gone, abridged` · full text: [screens.md](../screens.md)

> **Hard rule:** Tailwind utility classes are the only styling mechanism in this repository. shadcn/ui components are vendored as source under `src/Uniqua.Projector.Web/src/components/ui/` and edited in place, not installed as a dependency. A second styling mechanism — CSS modules, styled-components, inline style objects — is a review finding, not a preference.
>
> — `CLAUDE.md §Styling: Tailwind, once, verbatim` · full text: [CLAUDE.md](../../../../CLAUDE.md)

> **Hard rule:** Every dependency must be MIT, Apache-2.0, or BSD-style.
>
> — `CLAUDE.md §Licensing, abridged` · full text: [CLAUDE.md](../../../../CLAUDE.md)

**Fallback:** insufficient or contradicted by the code → read [screens.md](../screens.md) in full and the existing `components/ui/button.tsx` / `card.tsx`, and follow them. Do not guess.

## Data delta

No DB changes.

## API contract

Internal — no API surface.

## Acceptance criteria

### AC-16 — domain invariant

> **Given** a board member who writes a card title or description containing markup, a script or other formatting syntax
> **When** any member views that card
> **Then** the system shows exactly the characters that were typed, as plain text, and nothing in them is ever run or rendered as formatting — line breaks and repeated spaces are shown as typed, and a web address stays plain text rather than becoming a link
>
> — `spec.md §5, AC-16, verbatim` · full text: [spec.md](../spec.md)

## Checklist

- [ ] Vendor shadcn/ui `dialog.tsx` and `textarea.tsx` as source; add `@radix-ui/react-dialog` (MIT — confirm the licence in the lockfile) — `components/ui/`, `package.json`.
- [ ] Add `destructive` to `buttonVariants` using `--color-destructive` — `components/ui/button-variants.ts`.
- [ ] `PlainText({ text, as? })` — renders `{text}` as a child text node with `whitespace-pre-wrap break-words`; nothing else — `features/boards/PlainText.tsx`.
- [ ] `KeptTextNotice` — heading line, labelled kept fields through `PlainText`, and either «Apply again» + «Discard» or «Dismiss» — `features/boards/KeptTextNotice.tsx`.
- [ ] Component tests — `features/boards/__tests__/`.

## Edge cases

| Case | Behaviour |
|---|---|
| Text `<b>not bold</b>` | Shown as the literal characters; no `<b>` element in the DOM |
| Text `https://example.com` | Plain text; no `<a>` |
| Text with `\n` and two spaces | Line break and both spaces visible (`white-space: pre-wrap`) |
| A 150-character word with no spaces | Wraps inside its container (`break-words`), no sideways scroll |

## Definition of Done

- [ ] Component tests pass for every row above and for the Dialog's focus trap and Esc.
- [ ] No `dangerouslySetInnerHTML` anywhere under `features/boards/` (a test or lint rule asserts it).
- [ ] every Hard Rule inlined above still holds
- [ ] `npm --prefix src/Uniqua.Projector.Web run build` and `run lint` clean
