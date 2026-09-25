using Microsoft.Data.SqlClient;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Boards;

/// <summary>
/// T4 — the five tables the board tasks read and write: <c>Boards</c>, <c>Columns</c>, <c>Cards</c>,
/// <c>BoardMemberships</c> and <c>OwnedBoardCounters</c>. This task carries no acceptance criterion
/// of its own; the promise is structural — the generated migration matches
/// docs/features/boards-columns-cards/migrations/01_create_boards.up.sql in substance: same tables,
/// nullability, keys, delete behaviours and index definitions (data-model.md § Staged migrations).
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class BoardsSchemaTests(ApiFactory factory)
{
    [Theory]
    [InlineData("Boards")]
    [InlineData("Columns")]
    [InlineData("Cards")]
    [InlineData("BoardMemberships")]
    [InlineData("OwnedBoardCounters")]
    public async Task The_five_board_tables_are_created(string table)
    {
        Assert.Equal(1, await factory.TableCountAsync(table));
    }

    // ---- Columns, types and nullability (data-model.md § Entities) ------------------------------

    [Theory]
    [InlineData("Boards", "Id", "uniqueidentifier", false)]
    [InlineData("Boards", "Name", "nvarchar", false)]
    [InlineData("Boards", "CreatedAt", "datetimeoffset", false)]
    [InlineData("Boards", "CardCount", "int", false)]
    [InlineData("Boards", "ColumnLayoutVersion", "int", false)]
    [InlineData("Boards", "RowVersion", "timestamp", false)]
    [InlineData("Columns", "BoardId", "uniqueidentifier", false)]
    [InlineData("Columns", "Name", "nvarchar", false)]
    [InlineData("Columns", "Position", "int", false)]
    [InlineData("Columns", "CardCount", "int", false)]
    [InlineData("Columns", "NextCardPosition", "int", false)]
    [InlineData("Columns", "NameVersion", "int", false)]
    [InlineData("Cards", "BoardId", "uniqueidentifier", false)]
    [InlineData("Cards", "ColumnId", "uniqueidentifier", false)]
    [InlineData("Cards", "Position", "int", false)]
    [InlineData("Cards", "Title", "nvarchar", false)]
    [InlineData("Cards", "Description", "nvarchar", false)]
    [InlineData("Cards", "ContentVersion", "int", false)]
    [InlineData("BoardMemberships", "BoardId", "uniqueidentifier", false)]
    [InlineData("BoardMemberships", "AccountId", "uniqueidentifier", false)]
    [InlineData("BoardMemberships", "Role", "nvarchar", false)]
    [InlineData("OwnedBoardCounters", "AccountId", "uniqueidentifier", false)]
    [InlineData("OwnedBoardCounters", "OwnedBoardCount", "int", false)]
    [InlineData("OwnedBoardCounters", "RowVersion", "timestamp", false)]
    public async Task Each_column_the_data_model_declares_is_present(
        string table, string column, string sqlType, bool nullable)
    {
        Assert.Equal(sqlType, await factory.ScalarAsync<string>(
            $"""
            SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'
            """));
        Assert.Equal(nullable ? "YES" : "NO", await factory.ScalarAsync<string>(
            $"""
            SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'
            """));
    }

    [Theory]
    [InlineData("Boards", "Name", 200)]
    [InlineData("Columns", "Name", 100)]
    [InlineData("Cards", "Title", 300)]
    [InlineData("BoardMemberships", "Role", 10)]
    public async Task Text_columns_are_sized_at_twice_the_spec_limit(string table, string column, int maxLength)
    {
        Assert.Equal(maxLength, await factory.ScalarAsync<int>(
            $"""
            SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = '{table}' AND COLUMN_NAME = '{column}'
            """));
    }

    [Fact]
    public async Task Description_has_no_maximum_length()
    {
        // data-model.md: Description is the one text column that is not doubled, because it is
        // never bounded by nvarchar at all — the Text rule bounds it, not the storage width.
        Assert.Equal(-1, await factory.ScalarAsync<int>(
            """
            SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'Cards' AND COLUMN_NAME = 'Description'
            """));
    }

    // ---- Indexes (data-model.md § Indexes) -------------------------------------------------------

    [Fact]
    public async Task The_columns_position_index_exists_and_is_deliberately_not_unique()
    {
        Assert.Equal(1, await IndexExistsAsync("Columns", "IX_Columns_BoardId_Position"));
        Assert.Equal(0, await IndexFlagAsync("Columns", "IX_Columns_BoardId_Position", "is_unique"));
    }

    [Fact]
    public async Task The_card_summary_index_and_the_column_id_index_exist()
    {
        Assert.Equal(1, await IndexExistsAsync("Cards", "IX_Cards_BoardId_ColumnId_Position"));
        Assert.Equal(1, await IndexExistsAsync("Cards", "IX_Cards_ColumnId"));
    }

    [Fact]
    public async Task The_board_membership_pair_is_unique()
    {
        Assert.Equal(1, await IndexExistsAsync("BoardMemberships", "IX_BoardMemberships_BoardId_AccountId"));
        Assert.Equal(1, await IndexFlagAsync("BoardMemberships", "IX_BoardMemberships_BoardId_AccountId", "is_unique"));
    }

    [Fact]
    public async Task The_account_scoped_membership_index_exists()
    {
        Assert.Equal(1, await IndexExistsAsync("BoardMemberships", "IX_BoardMemberships_AccountId_BoardId"));
    }

    [Fact]
    public async Task The_one_owner_per_board_index_is_unique_and_filtered_to_the_owner_role()
    {
        Assert.Equal(1, await IndexExistsAsync("BoardMemberships", "IX_BoardMemberships_BoardId_Owner"));
        Assert.Equal(1, await IndexFlagAsync("BoardMemberships", "IX_BoardMemberships_BoardId_Owner", "is_unique"));
        Assert.Equal(1, await IndexFlagAsync("BoardMemberships", "IX_BoardMemberships_BoardId_Owner", "has_filter"));
    }

    // ---- Edge cases: the filtered and plain unique indexes as database facts --------------------

    [Fact]
    public async Task A_second_owner_membership_on_the_same_board_is_refused()
    {
        var boardId = Guid.CreateVersion7();
        var ownerId = Guid.CreateVersion7();
        var secondOwnerId = Guid.CreateVersion7();

        await InsertAccountAsync(ownerId);
        await InsertAccountAsync(secondOwnerId);
        await factory.ExecuteAsync(InsertBoardSql(boardId));
        await factory.ExecuteAsync(InsertMembershipSql(Guid.CreateVersion7(), boardId, ownerId, "Owner"));

        var refusal = await Assert.ThrowsAsync<SqlException>(() => factory.ExecuteAsync(
            InsertMembershipSql(Guid.CreateVersion7(), boardId, secondOwnerId, "Owner")));

        Assert.Contains("IX_BoardMemberships_BoardId_Owner", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_membership_for_the_same_board_and_account_is_refused()
    {
        var boardId = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();

        await InsertAccountAsync(accountId);
        await factory.ExecuteAsync(InsertBoardSql(boardId));
        await factory.ExecuteAsync(InsertMembershipSql(Guid.CreateVersion7(), boardId, accountId, "Member"));

        var refusal = await Assert.ThrowsAsync<SqlException>(() => factory.ExecuteAsync(
            InsertMembershipSql(Guid.CreateVersion7(), boardId, accountId, "Member")));

        Assert.Contains("IX_BoardMemberships_BoardId_AccountId", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_column_that_still_holds_a_card_is_refused()
    {
        var boardId = Guid.CreateVersion7();
        var columnId = Guid.CreateVersion7();

        await factory.ExecuteAsync(InsertBoardSql(boardId));
        await factory.ExecuteAsync(InsertColumnSql(columnId, boardId));
        await factory.ExecuteAsync(InsertCardSql(Guid.CreateVersion7(), boardId, columnId));

        var refusal = await Assert.ThrowsAsync<SqlException>(() => factory.ExecuteAsync(
            $"DELETE FROM [dbo].[Columns] WHERE [Id] = '{columnId}';"));

        Assert.Contains("FK_Cards_Columns_ColumnId", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Deleting_a_board_cascades_to_its_columns_cards_and_memberships()
    {
        var boardId = Guid.CreateVersion7();
        var columnId = Guid.CreateVersion7();
        var accountId = Guid.CreateVersion7();

        await InsertAccountAsync(accountId);
        await factory.ExecuteAsync(InsertBoardSql(boardId));
        await factory.ExecuteAsync(InsertColumnSql(columnId, boardId));
        await factory.ExecuteAsync(InsertCardSql(Guid.CreateVersion7(), boardId, columnId));
        await factory.ExecuteAsync(InsertMembershipSql(Guid.CreateVersion7(), boardId, accountId, "Owner"));

        await factory.ExecuteAsync($"DELETE FROM [dbo].[Boards] WHERE [Id] = '{boardId}';");

        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Cards] WHERE [BoardId] = '{boardId}'"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[BoardMemberships] WHERE [BoardId] = '{boardId}'"));
    }

    [Fact]
    public async Task Two_columns_at_the_same_position_mid_renumber_are_allowed()
    {
        // The position index is deliberately not unique: a renumber is one UPDATE per row, checked
        // per statement, so two columns briefly sharing a position must be legal at the store.
        var boardId = Guid.CreateVersion7();
        await factory.ExecuteAsync(InsertBoardSql(boardId));
        await factory.ExecuteAsync(InsertColumnSql(Guid.CreateVersion7(), boardId, position: 0));
        await factory.ExecuteAsync(InsertColumnSql(Guid.CreateVersion7(), boardId, position: 0));

        Assert.Equal(2, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Columns] WHERE [BoardId] = '{boardId}' AND [Position] = 0"));
    }

    // ---- Helpers ------------------------------------------------------------------------------

    private Task<int> IndexExistsAsync(string table, string index) =>
        factory.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM sys.indexes
            WHERE name = N'{index}' AND object_id = OBJECT_ID(N'[dbo].[{table}]')
            """);

    private Task<int> IndexFlagAsync(string table, string index, string flag) =>
        factory.ScalarAsync<int>(
            $"""
            SELECT CAST({flag} AS int) FROM sys.indexes
            WHERE name = N'{index}' AND object_id = OBJECT_ID(N'[dbo].[{table}]')
            """);

    private Task InsertAccountAsync(Guid accountId) =>
        factory.ExecuteAsync(SchemaQueries.InsertAccountSql(
            accountId, $"{Guid.NewGuid():N}@example.test", $"board-{Guid.NewGuid():N}"));

    private static string InsertBoardSql(Guid id) =>
        $"""
        INSERT INTO [dbo].[Boards] ([Id], [Name], [CreatedAt], [CardCount], [ColumnLayoutVersion])
        VALUES ('{id}', N'A board', SYSDATETIMEOFFSET(), 0, 1);
        """;

    private static string InsertColumnSql(Guid id, Guid boardId, int position = 0) =>
        $"""
        INSERT INTO [dbo].[Columns]
            ([Id], [BoardId], [Name], [Position], [CardCount], [NextCardPosition], [NameVersion])
        VALUES ('{id}', '{boardId}', N'To do', {position}, 0, 0, 1);
        """;

    private static string InsertCardSql(Guid id, Guid boardId, Guid columnId) =>
        $"""
        INSERT INTO [dbo].[Cards]
            ([Id], [BoardId], [ColumnId], [Position], [Title], [Description], [ContentVersion])
        VALUES ('{id}', '{boardId}', '{columnId}', 0, N'A card', N'', 1);
        """;

    private static string InsertMembershipSql(Guid id, Guid boardId, Guid accountId, string role) =>
        $"""
        INSERT INTO [dbo].[BoardMemberships] ([Id], [BoardId], [AccountId], [Role])
        VALUES ('{id}', '{boardId}', '{accountId}', N'{role}');
        """;
}
