---
status: Living
updated_at: "2026-09-24"
---

# Domain Context — Uniqua.Projector

## Glossary

- account — a registered identity with an email, a password and a display name, which a person signs in as. NOT board member (an account becomes a member only by being granted access to a specific board).
- board — a named workspace holding an ordered set of columns, created by one account who becomes its board owner, and visible only to its board members. NOT project (a project is at most a grouping of boards and grants no access of its own).
- board member — an account that has been granted access to one board and may read and change it. NOT account (an account that belongs to no board is still an account).
- board owner — the board member who created the board, and the only one who may rename it, delete it and invite others to it. NOT board member (every owner is a member, but most members are not owners).
- card — a unit of work with a title and an optional plain-text description, at one position in exactly one column of one board. NOT checklist item (a checklist item is a line of text inside one card, with no position on the board and no title or description of its own).
- column — a named lane at one position on one board, holding that board's cards in order; a board always keeps at least one. NOT card status (the members name and arrange columns freely, and the system attaches no meaning to any column's name, including the three a new board starts with).
- display name — the label other board members see next to an account's actions. NOT email (the email identifies the account at sign-in and is never shown to other members).
- invited member — a board member who joined through an invitation link rather than by creating the board. NOT board owner (an invited member never holds the board's invitation rights).
- live-update connection — the connection a board member's browser holds open to one board so that changes made by other members arrive without a reload. NOT session (one session opens and closes many such connections, and each is authorised by the session that opened it).
- session — the period during which a browser is recognised as a specific account, carried by a cookie that page scripts cannot read. NOT account (one account may hold several sessions, on several devices, at once).
- visitor — a person using the application with no active session. NOT account (a visitor may already own an account and simply not be signed in).
