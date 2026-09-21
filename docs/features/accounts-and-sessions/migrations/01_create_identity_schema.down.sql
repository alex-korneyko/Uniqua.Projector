-- accounts-and-sessions · staged migration 01 of 03 · DOWN
-- Reverses 01_create_identity_schema.up.sql exactly: every CREATE TABLE has a DROP TABLE, every
-- CREATE INDEX a DROP INDEX. Tables are dropped children-first so no foreign key blocks the drop.
--
-- Dropping AspNetUsers destroys every account and, through the ON DELETE CASCADE in migration 02,
-- every session with it. This is a development-time reversal, not an operation to run against a
-- database anyone has registered on.

-- Indexes -------------------------------------------------------------------------------------------

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'DisplayNameIndex' AND object_id = OBJECT_ID(N'[dbo].[AspNetUsers]'))
    DROP INDEX [DisplayNameIndex] ON [dbo].[AspNetUsers];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'EmailIndex' AND object_id = OBJECT_ID(N'[dbo].[AspNetUsers]'))
    DROP INDEX [EmailIndex] ON [dbo].[AspNetUsers];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UserNameIndex' AND object_id = OBJECT_ID(N'[dbo].[AspNetUsers]'))
    DROP INDEX [UserNameIndex] ON [dbo].[AspNetUsers];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'RoleNameIndex' AND object_id = OBJECT_ID(N'[dbo].[AspNetRoles]'))
    DROP INDEX [RoleNameIndex] ON [dbo].[AspNetRoles];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetUserRoles_RoleId' AND object_id = OBJECT_ID(N'[dbo].[AspNetUserRoles]'))
    DROP INDEX [IX_AspNetUserRoles_RoleId] ON [dbo].[AspNetUserRoles];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetUserLogins_UserId' AND object_id = OBJECT_ID(N'[dbo].[AspNetUserLogins]'))
    DROP INDEX [IX_AspNetUserLogins_UserId] ON [dbo].[AspNetUserLogins];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetUserClaims_UserId' AND object_id = OBJECT_ID(N'[dbo].[AspNetUserClaims]'))
    DROP INDEX [IX_AspNetUserClaims_UserId] ON [dbo].[AspNetUserClaims];

IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetRoleClaims_RoleId' AND object_id = OBJECT_ID(N'[dbo].[AspNetRoleClaims]'))
    DROP INDEX [IX_AspNetRoleClaims_RoleId] ON [dbo].[AspNetRoleClaims];

-- Tables --------------------------------------------------------------------------------------------

IF OBJECT_ID(N'[dbo].[AspNetUserTokens]', N'U') IS NOT NULL DROP TABLE [dbo].[AspNetUserTokens];
IF OBJECT_ID(N'[dbo].[AspNetUserRoles]',  N'U') IS NOT NULL DROP TABLE [dbo].[AspNetUserRoles];
IF OBJECT_ID(N'[dbo].[AspNetUserLogins]', N'U') IS NOT NULL DROP TABLE [dbo].[AspNetUserLogins];
IF OBJECT_ID(N'[dbo].[AspNetUserClaims]', N'U') IS NOT NULL DROP TABLE [dbo].[AspNetUserClaims];
IF OBJECT_ID(N'[dbo].[AspNetRoleClaims]', N'U') IS NOT NULL DROP TABLE [dbo].[AspNetRoleClaims];
IF OBJECT_ID(N'[dbo].[AspNetUsers]',      N'U') IS NOT NULL DROP TABLE [dbo].[AspNetUsers];
IF OBJECT_ID(N'[dbo].[AspNetRoles]',      N'U') IS NOT NULL DROP TABLE [dbo].[AspNetRoles];
