# Uniqua.Projector

A Kanban board: accounts, boards, columns, cards, checklist items, invitations, and live updates
pushed to everyone viewing a board.

## Session rules — read this first

Every rule governing a session is written down and checkable from outside the application. In one
line each:

- **How a session is carried** — an `HttpOnly`, `Secure`, same-site cookie holding an opaque
  reference to a server-side record. No page script can read it and no endpoint returns it.
- **How long it lives** — 14 days from the last request, and never more than 90 days from when it
  was opened, however actively it is used.
- **What ends it** — signing out (exactly the session it travelled on, and no other), or either time
  limit. A redeploy ends nothing.
- **What happens when someone guesses at a password** — a per-account delay that grows with each
  consecutive failure, resetting on success or after 15 minutes idle, plus a cap of 20 failed
  sign-ins or attempts still in flight per (request source, address) in that window — past it, `429`
  until it clears. A correct password is never delayed; the cap can still refuse the owner for
  up to 15 minutes if a guesser shares their request source and targets their address
  (accepted, spec §6.1).

**→ [`docs/session-rules.md`](docs/session-rules.md)** has each of those with the file that enforces
it, the test that proves it, and how to verify it yourself.

## Getting started

| What | Command |
|---|---|
| Build the server | `dotnet build Uniqua.Projector.slnx` |
| Test the server | `dotnet test Uniqua.Projector.slnx` |
| Check formatting | `dotnet format --verify-no-changes` |
| Build the client | `npm --prefix src/Uniqua.Projector.Web run build` |
| Test the client | `npm --prefix src/Uniqua.Projector.Web run test` |
| Lint the client | `npm --prefix src/Uniqua.Projector.Web run lint` |

The integration tests start a SQL Server container, so Docker has to be running. That cost is
accepted knowingly: an in-memory provider would not exercise the store this application ships
against.

## How it is put together

`Api → Application → Domain` and `Infrastructure → Application → Domain`. Domain references nothing;
Api references Infrastructure only to register implementations at startup. The client is built into
the API's `wwwroot` and served from the same origin, because the session cookie is httpOnly and
same-origin — splitting them across hosts would break authentication.

- [`docs/architecture-map.md`](docs/architecture-map.md) — the stack, the conventions, and the
  closest precedent to copy.
- [`CLAUDE.md`](CLAUDE.md) — the same decisions restated as working rules.
- [`docs/adr/`](docs/adr/) — the foundational decisions.
- [`docs/roadmap.md`](docs/roadmap.md) — what is built, and what comes next.

## Contracts

Each feature's API contract lives beside it, and is the contract of record:

- [`docs/features/accounts-and-sessions/contracts/openapi.yaml`](docs/features/accounts-and-sessions/contracts/openapi.yaml)

Every failure leaves this application as an RFC 9457 `application/problem+json` document carrying a
stable `code`, produced by one handler. No endpoint builds an error shape of its own.
