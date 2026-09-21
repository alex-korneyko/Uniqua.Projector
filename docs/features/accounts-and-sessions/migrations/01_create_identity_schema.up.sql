-- accounts-and-sessions · staged migration 01 of 03 · UP
-- Creates the ASP.NET Core Identity schema at its framework defaults (confirmed with the owner
-- 2026-09-21: the full default set of seven tables is kept, not trimmed), plus this feature's three
-- additions to the user table: DisplayName, NormalizedDisplayName and LastFailedAttemptAt.
--
-- STAGED, NOT LIVE. The live migration is an EF Core migration generated from the model
-- (`dotnet ef migrations add CreateIdentitySchema`); this file is the reviewed statement of what that
-- generated SQL must say. See data-model.md § Promotion.
--
-- Keys are uniqueidentifier because the context is IdentityDbContext<Account, IdentityRole<Guid>, Guid>
-- and identifiers are application-generated with Guid.CreateVersion7() (architecture-map §Conventions).

IF OBJECT_ID(N'[dbo].[AspNetRoles]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetRoles] (
        [Id]               uniqueidentifier NOT NULL,
        [Name]             nvarchar(256)    NULL,
        [NormalizedName]   nvarchar(256)    NULL,
        [ConcurrencyStamp] nvarchar(max)    NULL,
        CONSTRAINT [PK_AspNetRoles] PRIMARY KEY ([Id])
    );
END;

IF OBJECT_ID(N'[dbo].[AspNetUsers]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetUsers] (
        -- Identity defaults
        [Id]                    uniqueidentifier NOT NULL,
        [UserName]              nvarchar(256)    NULL,
        [NormalizedUserName]    nvarchar(256)    NULL,
        [Email]                 nvarchar(256)    NULL,
        [NormalizedEmail]       nvarchar(256)    NULL,
        [EmailConfirmed]        bit              NOT NULL,
        [PasswordHash]          nvarchar(max)    NULL,
        [SecurityStamp]         nvarchar(max)    NULL,
        [ConcurrencyStamp]      nvarchar(max)    NULL,
        [PhoneNumber]           nvarchar(max)    NULL,
        [PhoneNumberConfirmed]  bit              NOT NULL,
        [TwoFactorEnabled]      bit              NOT NULL,
        [LockoutEnd]            datetimeoffset   NULL,
        [LockoutEnabled]        bit              NOT NULL,
        [AccessFailedCount]     int              NOT NULL,
        -- this feature's additions
        [DisplayName]           nvarchar(50)     NOT NULL,  -- AC-01: at most 50 characters
        [NormalizedDisplayName] nvarchar(50)     NOT NULL,  -- AC-11/AC-11b: the comparison key
        [LastFailedAttemptAt]   datetimeoffset   NULL,      -- AC-12: the 15-minute reset is derived from this
        CONSTRAINT [PK_AspNetUsers] PRIMARY KEY ([Id])
    );
END;

IF OBJECT_ID(N'[dbo].[AspNetRoleClaims]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetRoleClaims] (
        [Id]         int              NOT NULL IDENTITY,
        [RoleId]     uniqueidentifier NOT NULL,
        [ClaimType]  nvarchar(max)    NULL,
        [ClaimValue] nvarchar(max)    NULL,
        CONSTRAINT [PK_AspNetRoleClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetRoleClaims_AspNetRoles_RoleId]
            FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[dbo].[AspNetUserClaims]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetUserClaims] (
        [Id]         int              NOT NULL IDENTITY,
        [UserId]     uniqueidentifier NOT NULL,
        [ClaimType]  nvarchar(max)    NULL,
        [ClaimValue] nvarchar(max)    NULL,
        CONSTRAINT [PK_AspNetUserClaims] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_AspNetUserClaims_AspNetUsers_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[dbo].[AspNetUserLogins]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetUserLogins] (
        [LoginProvider]       nvarchar(450)    NOT NULL,
        [ProviderKey]         nvarchar(450)    NOT NULL,
        [ProviderDisplayName] nvarchar(max)    NULL,
        [UserId]              uniqueidentifier NOT NULL,
        CONSTRAINT [PK_AspNetUserLogins] PRIMARY KEY ([LoginProvider], [ProviderKey]),
        CONSTRAINT [FK_AspNetUserLogins_AspNetUsers_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[dbo].[AspNetUserRoles]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetUserRoles] (
        [UserId] uniqueidentifier NOT NULL,
        [RoleId] uniqueidentifier NOT NULL,
        CONSTRAINT [PK_AspNetUserRoles] PRIMARY KEY ([UserId], [RoleId]),
        CONSTRAINT [FK_AspNetUserRoles_AspNetRoles_RoleId]
            FOREIGN KEY ([RoleId]) REFERENCES [dbo].[AspNetRoles] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_AspNetUserRoles_AspNetUsers_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[dbo].[AspNetUserTokens]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[AspNetUserTokens] (
        [UserId]        uniqueidentifier NOT NULL,
        [LoginProvider] nvarchar(450)    NOT NULL,
        [Name]          nvarchar(450)    NOT NULL,
        [Value]         nvarchar(max)    NULL,
        CONSTRAINT [PK_AspNetUserTokens] PRIMARY KEY ([UserId], [LoginProvider], [Name]),
        CONSTRAINT [FK_AspNetUserTokens_AspNetUsers_UserId]
            FOREIGN KEY ([UserId]) REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

-- Indexes -------------------------------------------------------------------------------------------

-- FK hygiene on the cascade paths (Identity's own defaults).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetRoleClaims_RoleId' AND object_id = OBJECT_ID(N'[dbo].[AspNetRoleClaims]'))
    CREATE INDEX [IX_AspNetRoleClaims_RoleId] ON [dbo].[AspNetRoleClaims] ([RoleId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetUserClaims_UserId' AND object_id = OBJECT_ID(N'[dbo].[AspNetUserClaims]'))
    CREATE INDEX [IX_AspNetUserClaims_UserId] ON [dbo].[AspNetUserClaims] ([UserId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetUserLogins_UserId' AND object_id = OBJECT_ID(N'[dbo].[AspNetUserLogins]'))
    CREATE INDEX [IX_AspNetUserLogins_UserId] ON [dbo].[AspNetUserLogins] ([UserId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'IX_AspNetUserRoles_RoleId' AND object_id = OBJECT_ID(N'[dbo].[AspNetUserRoles]'))
    CREATE INDEX [IX_AspNetUserRoles_RoleId] ON [dbo].[AspNetUserRoles] ([RoleId]);

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'RoleNameIndex' AND object_id = OBJECT_ID(N'[dbo].[AspNetRoles]'))
    CREATE UNIQUE INDEX [RoleNameIndex] ON [dbo].[AspNetRoles] ([NormalizedName]) WHERE [NormalizedName] IS NOT NULL;

-- Identity's own login lookup. UserName holds the email address; it is NOT the display name.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'UserNameIndex' AND object_id = OBJECT_ID(N'[dbo].[AspNetUsers]'))
    CREATE UNIQUE INDEX [UserNameIndex] ON [dbo].[AspNetUsers] ([NormalizedUserName]) WHERE [NormalizedUserName] IS NOT NULL;

-- AC-03: an email address identifies exactly one account. Identity's default EmailIndex is NON-unique;
-- this feature makes it unique. Filtered, because Identity leaves NormalizedEmail nullable and the
-- default column nullability is kept.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'EmailIndex' AND object_id = OBJECT_ID(N'[dbo].[AspNetUsers]'))
    CREATE UNIQUE INDEX [EmailIndex] ON [dbo].[AspNetUsers] ([NormalizedEmail]) WHERE [NormalizedEmail] IS NOT NULL;

-- AC-11 / AC-11b: a display name identifies exactly one account to the people who see it.
-- Not filtered: NormalizedDisplayName is NOT NULL.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = N'DisplayNameIndex' AND object_id = OBJECT_ID(N'[dbo].[AspNetUsers]'))
    CREATE UNIQUE INDEX [DisplayNameIndex] ON [dbo].[AspNetUsers] ([NormalizedDisplayName]);
