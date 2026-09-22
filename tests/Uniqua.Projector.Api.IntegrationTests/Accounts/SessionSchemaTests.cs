using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T4 — the Sessions table. ADR 0008 chose a server-side record over a self-contained cookie
/// ticket, and this is the physical consequence: the three timestamps the four session rules are
/// evaluated against, and a revoked row that is kept rather than deleted, so AC-10 is answered by
/// a record rather than by an absence.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SessionSchemaTests(ApiFactory factory)
{
    [Fact]
    public async Task The_sessions_table_exists()
    {
        Assert.Equal(1, await factory.TableCountAsync("Sessions"));
    }

    [Theory]
    [InlineData("Id", "uniqueidentifier", false)]
    [InlineData("AccountId", "uniqueidentifier", false)]
    [InlineData("CreatedAt", "datetimeoffset", false)]
    [InlineData("LastSeenAt", "datetimeoffset", false)]
    [InlineData("RevokedAt", "datetimeoffset", true)]
    public async Task Each_column_the_session_rules_are_evaluated_against_is_present(
        string column, string sqlType, bool nullable)
    {
        Assert.Equal(sqlType, await factory.ScalarAsync<string>(
            $"""
            SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'Sessions' AND COLUMN_NAME = '{column}'
            """));
        Assert.Equal(nullable ? "YES" : "NO", await factory.ScalarAsync<string>(
            $"""
            SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'Sessions' AND COLUMN_NAME = '{column}'
            """));
    }

    [Fact]
    public async Task The_table_carries_no_column_beyond_the_five_the_data_model_declares()
    {
        var columns = await factory.ScalarAsync<string>(
            """
            SELECT STRING_AGG(COLUMN_NAME, ',') WITHIN GROUP (ORDER BY COLUMN_NAME)
            FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'Sessions'
            """);

        Assert.Equal("AccountId,CreatedAt,Id,LastSeenAt,RevokedAt", columns);
    }

    // ---- The three indexes, and the one deliberately absent -----------------------------------

    [Theory]
    [InlineData("IX_Sessions_AccountId", false)]
    [InlineData("IX_Sessions_CreatedAt", false)]
    [InlineData("IX_Sessions_RevokedAt", true)]
    public async Task The_three_declared_indexes_exist_with_the_filter_each_should_have(
        string index, bool filtered)
    {
        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"""
            SELECT COUNT(*) FROM sys.indexes
            WHERE name = N'{index}' AND object_id = OBJECT_ID(N'[dbo].[Sessions]')
            """));

        // IX_Sessions_RevokedAt is filtered so it stays a fraction of the table and costs nothing
        // on the hot path of opening a session, where RevokedAt is NULL.
        Assert.Equal(filtered ? 1 : 0, await factory.ScalarAsync<int>(
            $"""
            SELECT CAST(has_filter AS int) FROM sys.indexes
            WHERE name = N'{index}' AND object_id = OBJECT_ID(N'[dbo].[Sessions]')
            """));
    }

    [Fact]
    public async Task Last_seen_at_is_deliberately_left_unindexed()
    {
        // It is written on the hot path and never filtered on: the 14-day rule is evaluated on a
        // row already fetched by primary key. An index here would cost every request a write.
        var indexedColumns = await factory.ScalarAsync<int>(
            """
            SELECT COUNT(*)
            FROM sys.index_columns ic
            JOIN sys.columns c ON c.object_id = ic.object_id AND c.column_id = ic.column_id
            WHERE ic.object_id = OBJECT_ID(N'[dbo].[Sessions]') AND c.name = N'LastSeenAt'
            """);

        Assert.Equal(0, indexedColumns);

        // Without this the assertion above is satisfied by the table not existing at all, which
        // would make it a test that can never fail for the reason it was written.
        Assert.True(await factory.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[Sessions]') AND name IS NOT NULL
            """) >= 3);
    }

    // ---- The foreign key ------------------------------------------------------------------------

    [Fact]
    public async Task Deleting_an_account_takes_its_sessions_with_it()
    {
        var accountId = Guid.CreateVersion7();
        await factory.ExecuteAsync(SchemaQueries.InsertAccountSql(
            accountId, $"{Guid.NewGuid():N}@example.test", $"cascade-{Guid.NewGuid():N}"));
        await factory.ExecuteAsync(InsertSessionSql(Guid.CreateVersion7(), accountId));
        await factory.ExecuteAsync(InsertSessionSql(Guid.CreateVersion7(), accountId));

        Assert.Equal(2, await SessionCountAsync(accountId));

        await factory.ExecuteAsync($"DELETE FROM [dbo].[AspNetUsers] WHERE [Id] = '{accountId}';");

        Assert.Equal(0, await SessionCountAsync(accountId));
    }

    [Fact]
    public async Task A_session_for_an_account_that_does_not_exist_is_refused()
    {
        var refusal = await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() =>
            factory.ExecuteAsync(InsertSessionSql(Guid.CreateVersion7(), Guid.CreateVersion7())));

        // Naming the constraint matters: without it a missing table would satisfy this test with
        // an "invalid object name" and the foreign key would never actually be proven.
        Assert.Contains("FK_Sessions_AspNetUsers_AccountId", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_account_has_no_navigation_back_to_its_sessions()
    {
        // data-model.md is explicit: no navigation property, so reading a session can never drag
        // the whole account graph along with it. The model is asserted, not the schema.
        var account = typeof(Infrastructure.Accounts.ProjectorUser);

        Assert.DoesNotContain(
            account.GetProperties(),
            property => property.PropertyType.FullName?.Contains("Session", StringComparison.Ordinal) == true);
        await Task.CompletedTask;
    }

    [Fact]
    public async Task Two_sessions_of_one_account_are_independent_rows()
    {
        var accountId = Guid.CreateVersion7();
        await factory.ExecuteAsync(SchemaQueries.InsertAccountSql(
            accountId, $"{Guid.NewGuid():N}@example.test", $"devices-{Guid.NewGuid():N}"));

        var onePhone = Guid.CreateVersion7();
        var oneLaptop = Guid.CreateVersion7();
        await factory.ExecuteAsync(InsertSessionSql(onePhone, accountId));
        await factory.ExecuteAsync(InsertSessionSql(oneLaptop, accountId));

        await factory.ExecuteAsync(
            $"UPDATE [dbo].[Sessions] SET [RevokedAt] = SYSDATETIMEOFFSET() WHERE [Id] = '{onePhone}';");

        Assert.Equal(1, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Sessions] WHERE [Id] = '{oneLaptop}' AND [RevokedAt] IS NULL"));
        Assert.Equal(0, await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Sessions] WHERE [Id] = '{onePhone}' AND [RevokedAt] IS NULL"));
    }

    private Task<int> SessionCountAsync(Guid accountId) =>
        factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Sessions] WHERE [AccountId] = '{accountId}'");

    private static string InsertSessionSql(Guid id, Guid accountId) =>
        $"""
        INSERT INTO [dbo].[Sessions] ([Id], [AccountId], [CreatedAt], [LastSeenAt], [RevokedAt])
        VALUES ('{id}', '{accountId}', SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), NULL);
        """;
}
