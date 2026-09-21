-- accounts-and-sessions · staged migration 02 of 03 · DOWN
-- Reverses 02_create_sessions.up.sql exactly. Dropping the table ends every live session: every browser
-- holding a cookie is presented the sign-in form on its next request, which is AC-10 behaving correctly
-- rather than a fault.

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sessions_RevokedAt' AND object_id = OBJECT_ID(N'[dbo].[Sessions]'))
    DROP INDEX [IX_Sessions_RevokedAt] ON [dbo].[Sessions];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sessions_CreatedAt' AND object_id = OBJECT_ID(N'[dbo].[Sessions]'))
    DROP INDEX [IX_Sessions_CreatedAt] ON [dbo].[Sessions];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_Sessions_AccountId' AND object_id = OBJECT_ID(N'[dbo].[Sessions]'))
    DROP INDEX [IX_Sessions_AccountId] ON [dbo].[Sessions];

IF OBJECT_ID(N'[dbo].[Sessions]', N'U') IS NOT NULL
    DROP TABLE [dbo].[Sessions];
