-- boards-columns-cards · 01 create boards (STAGED — review target, not applied from here)
-- Promoted by `implement` as the EF Core migration `<yyyyMMddHHmmss>_CreateBoards`, generated from
-- the model with `dotnet ef migrations add CreateBoards`; its `migrations script` output must match
-- this file in substance. See docs/features/boards-columns-cards/data-model.md.
--
-- Text columns are sized at 2 x the spec limit: the spec counts code points, nvarchar counts UTF-16
-- code units. The real limits are enforced by the domain (BoardText), not here.
-- No CHECK constraints and no DEFAULTs: rules live in Domain, values come from the application.

SET XACT_ABORT ON;
BEGIN TRANSACTION;

IF OBJECT_ID(N'[dbo].[Boards]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Boards] (
        [Id]                  uniqueidentifier NOT NULL,
        [Name]                nvarchar(200)    NOT NULL,
        [CreatedAt]           datetimeoffset   NOT NULL,
        [CardCount]           int              NOT NULL,
        [ColumnLayoutVersion] int              NOT NULL,
        [RowVersion]          rowversion       NOT NULL,
        CONSTRAINT [PK_Boards] PRIMARY KEY ([Id])
    );
END;

IF OBJECT_ID(N'[dbo].[Columns]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Columns] (
        [Id]               uniqueidentifier NOT NULL,
        [BoardId]          uniqueidentifier NOT NULL,
        [Name]             nvarchar(100)    NOT NULL,
        [Position]         int              NOT NULL,
        [CardCount]        int              NOT NULL,
        [NextCardPosition] int              NOT NULL,
        [NameVersion]      int              NOT NULL,
        CONSTRAINT [PK_Columns] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Columns_Boards_BoardId] FOREIGN KEY ([BoardId])
            REFERENCES [dbo].[Boards] ([Id]) ON DELETE CASCADE
    );
END;

IF OBJECT_ID(N'[dbo].[Cards]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[Cards] (
        [Id]             uniqueidentifier NOT NULL,
        [BoardId]        uniqueidentifier NOT NULL,
        [ColumnId]       uniqueidentifier NOT NULL,
        [Position]       int              NOT NULL,
        [Title]          nvarchar(300)    NOT NULL,
        [Description]    nvarchar(max)    NOT NULL,
        [ContentVersion] int              NOT NULL,
        CONSTRAINT [PK_Cards] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_Cards_Boards_BoardId] FOREIGN KEY ([BoardId])
            REFERENCES [dbo].[Boards] ([Id]) ON DELETE CASCADE,
        -- NO ACTION: a second cascade path into Cards is refused by SQL Server, and the store then
        -- backs up AC-09 (a column row that still has cards cannot be deleted).
        CONSTRAINT [FK_Cards_Columns_ColumnId] FOREIGN KEY ([ColumnId])
            REFERENCES [dbo].[Columns] ([Id]) ON DELETE NO ACTION
    );
END;

IF OBJECT_ID(N'[dbo].[BoardMemberships]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[BoardMemberships] (
        [Id]        uniqueidentifier NOT NULL,
        [BoardId]   uniqueidentifier NOT NULL,
        [AccountId] uniqueidentifier NOT NULL,
        [Role]      nvarchar(10)     NOT NULL,
        CONSTRAINT [PK_BoardMemberships] PRIMARY KEY ([Id]),
        CONSTRAINT [FK_BoardMemberships_Boards_BoardId] FOREIGN KEY ([BoardId])
            REFERENCES [dbo].[Boards] ([Id]) ON DELETE CASCADE,
        -- NO ACTION: cascading an account deletion would leave an ownerless, invisible board.
        CONSTRAINT [FK_BoardMemberships_AspNetUsers_AccountId] FOREIGN KEY ([AccountId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE NO ACTION
    );
END;

IF OBJECT_ID(N'[dbo].[OwnedBoardCounters]', N'U') IS NULL
BEGIN
    CREATE TABLE [dbo].[OwnedBoardCounters] (
        [AccountId]       uniqueidentifier NOT NULL,
        [OwnedBoardCount] int              NOT NULL,
        [RowVersion]      rowversion       NOT NULL,
        CONSTRAINT [PK_OwnedBoardCounters] PRIMARY KEY ([AccountId]),
        CONSTRAINT [FK_OwnedBoardCounters_AspNetUsers_AccountId] FOREIGN KEY ([AccountId])
            REFERENCES [dbo].[AspNetUsers] ([Id]) ON DELETE CASCADE
    );
END;

-- Board's columns in position order (flows 2, 5-9); FK Columns.BoardId.
-- Deliberately NOT unique: a renumber is one UPDATE per row, checked per statement (ADR 0017).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Columns_BoardId_Position' AND [object_id] = OBJECT_ID(N'[dbo].[Columns]'))
    CREATE INDEX [IX_Columns_BoardId_Position] ON [dbo].[Columns] ([BoardId], [Position]);

-- Card summaries when opening a board (flow 5, ADR 0018), covered; FK Cards.BoardId.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Cards_BoardId_ColumnId_Position' AND [object_id] = OBJECT_ID(N'[dbo].[Cards]'))
    CREATE INDEX [IX_Cards_BoardId_ColumnId_Position] ON [dbo].[Cards] ([BoardId], [ColumnId], [Position])
        INCLUDE ([Title], [ContentVersion]);

-- FK Cards.ColumnId: the NO ACTION check on every column deletion (flow 8).
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_Cards_ColumnId' AND [object_id] = OBJECT_ID(N'[dbo].[Cards]'))
    CREATE INDEX [IX_Cards_ColumnId] ON [dbo].[Cards] ([ColumnId]);

-- Member-scoped load (ADR 0014); one record per (board, account) (ADR 0013); FK BoardId.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_BoardMemberships_BoardId_AccountId' AND [object_id] = OBJECT_ID(N'[dbo].[BoardMemberships]'))
    CREATE UNIQUE INDEX [IX_BoardMemberships_BoardId_AccountId] ON [dbo].[BoardMemberships] ([BoardId], [AccountId]);

-- "My boards" with the owned flag (flow 4, AC-04), covered; FK AccountId.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_BoardMemberships_AccountId_BoardId' AND [object_id] = OBJECT_ID(N'[dbo].[BoardMemberships]'))
    CREATE INDEX [IX_BoardMemberships_AccountId_BoardId] ON [dbo].[BoardMemberships] ([AccountId], [BoardId])
        INCLUDE ([Role]);

-- Exactly one Owner per board (ADR 0013), as a database fact.
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE [name] = N'IX_BoardMemberships_BoardId_Owner' AND [object_id] = OBJECT_ID(N'[dbo].[BoardMemberships]'))
    CREATE UNIQUE INDEX [IX_BoardMemberships_BoardId_Owner] ON [dbo].[BoardMemberships] ([BoardId])
        WHERE [Role] = N'Owner';

COMMIT TRANSACTION;
