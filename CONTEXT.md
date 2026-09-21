---
status: Living
updated_at: "2026-09-21"
---

# Domain Context — Uniqua.Projector

## Glossary

- account — a registered identity with an email, a password and a display name, which a person signs in as. NOT board member (an account becomes a member only by being granted access to a specific board).
- board member — an account that has been granted access to one board and may read and change it. NOT account (an account that belongs to no board is still an account).
- board owner — the board member who created the board and may invite others to it. NOT board member (every owner is a member, but most members are not owners).
- display name — the label other board members see next to an account's actions. NOT email (the email identifies the account at sign-in and is never shown to other members).
- invited member — a board member who joined through an invitation link rather than by creating the board. NOT board owner (an invited member never holds the board's invitation rights).
- live-update connection — the connection a board member's browser holds open to one board so that changes made by other members arrive without a reload. NOT session (one session opens and closes many such connections, and each is authorised by the session that opened it).
- session — the period during which a browser is recognised as a specific account, carried by a cookie that page scripts cannot read. NOT account (one account may hold several sessions, on several devices, at once).
- visitor — a person using the application with no active session. NOT account (a visitor may already own an account and simply not be signed in).
