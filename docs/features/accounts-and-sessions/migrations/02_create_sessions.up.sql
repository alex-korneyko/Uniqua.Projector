-- accounts-and-sessions · staged migration 02 of 03 · UP
-- The session record of ADR 0008: one row per session, so that ending a session is a server-side fact
-- rather than the deletion of a cookie the browser may have copied. Depends on migration 01.
--
-- STAGED, NOT LIVE. The live migration is `dotnet ef migrations add CreateSessions`; this file is the
-- reviewed statement of what that generated SQL must say. See data-model.md § Promotion.

IF OBJECT_ID(N'[dbo].[Sessions]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Sessions] (
        -- The opaque reference the session cookie carries. Guid.CreateVersion7(), application-generated.
        [Id]         uniqueidentifier NOT NULL,
        [AccountId]  uniqueidentifier NOT NULL,
        -- AC-07b: no session is recognised more than 90 days after this instant.
        [CreatedAt]  datetimeoffset   NOT NULL,
        -- AC-07: the session ends after 14 days without a request. Written at most once an hour.
        [LastSeenAt] datetimeoffset   NOT NULL,
        -- AC-08 / AC-10: NULL means live. A revoked row is kept until the sweep removes it.
        [RevokedAt]  datetimeoffset   NULL,
        CONSTRAINT [PK_Sessions] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Sessions_AspNetUsers_AccountId]
            FOREIGN KEY ([AccountId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

-- No CHECK constraint on the ordering of the three timestamps: the 14-day and 90-day rules belong to
-- the Session entity in Domain (sad §5), and restating them here would put one rule in two places.

-- Indexes -------------------------------------------------------------------------------------------

-- FK hygiene: the ON DELETE CASCADE from AspNetUsers finds this account's sessions by AccountId.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sessions_AccountId' AND object_id = OBJECT_ID(N'[dbo].[Sessions]'))
    CREATE INDEX [IX_Sessions_AccountId] ON [dbo].[Sessions] ([AccountId]);

-- sad §6 flow 7: "Delete sessions opened more than 90 days ago".
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sessions_CreatedAt' AND object_id = OBJECT_ID(N'[dbo].[Sessions]'))
    CREATE INDEX [IX_Sessions_CreatedAt] ON [dbo].[Sessions] ([CreatedAt]);

-- sad §6 flow 7: "... or revoked more than 14 days ago". Filtered, because the overwhelming majority of
-- rows are live and carry NULL here: the index stays a fraction of the table and costs nothing on the
-- common write path of opening a session.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sessions_RevokedAt' AND object_id = OBJECT_ID(N'[dbo].[Sessions]'))
    CREATE INDEX [IX_Sessions_RevokedAt] ON [dbo].[Sessions] ([RevokedAt]) WHERE [RevokedAt] IS NOT NULL;

-- Deliberately NOT indexed: LastSeenAt. It is written on the hot path and never filtered on — the
-- 14-day rule is evaluated on a row already fetched by primary key (sad §6 flow 5).
