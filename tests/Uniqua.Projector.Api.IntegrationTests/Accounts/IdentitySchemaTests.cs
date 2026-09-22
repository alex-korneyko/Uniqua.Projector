using Microsoft.Data.SqlClient;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T1 — the identity schema. AC-03 ("an email address identifies exactly one account") and AC-11b
/// ("a display name identifies exactly one account") are promises the database itself has to keep:
/// the plain-language refusals are T8's and T13's half, but if two colliding rows can physically
/// land then no amount of application checking makes the promise true. AC-13 ("exactly one stable
/// identity to record") is the uniqueidentifier primary key the application fills.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class IdentitySchemaTests(ApiFactory factory)
{
    [Theory]
    [InlineData("AspNetUsers")]
    [InlineData("AspNetRoles")]
    [InlineData("AspNetUserRoles")]
    [InlineData("AspNetUserClaims")]
    [InlineData("AspNetRoleClaims")]
    [InlineData("AspNetUserLogins")]
    [InlineData("AspNetUserTokens")]
    public async Task The_full_default_identity_table_set_is_created(string table)
    {
        Assert.Equal(1, await factory.TableCountAsync(table));
    }

    [Theory]
    // column, sql type, nullable, max length in characters (-1 where the type is fixed width)
    [InlineData("Id", "uniqueidentifier", false, -1)]
    [InlineData("DisplayName", "nvarchar", false, 50)]
    [InlineData("NormalizedDisplayName", "nvarchar", false, 50)]
    [InlineData("LastFailedAttemptAt", "datetimeoffset", true, -1)]
    [InlineData("NormalizedEmail", "nvarchar", true, 256)]
    [InlineData("AccessFailedCount", "int", false, -1)]
    public async Task The_account_table_carries_this_features_columns(
        string column, string sqlType, bool nullable, int maxLength)
    {
        var actualType = await factory.ScalarAsync<string>(
            $"""
            SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = '{column}'
            """);
        var actualNullable = await factory.ScalarAsync<string>(
            $"""
            SELECT IS_NULLABLE FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = '{column}'
            """);

        Assert.Equal(sqlType, actualType);
        Assert.Equal(nullable ? "YES" : "NO", actualNullable);

        if (maxLength > 0)
        {
            Assert.Equal(
                maxLength,
                await factory.ScalarAsync<int>(
                    $"""
                    SELECT CHARACTER_MAXIMUM_LENGTH FROM INFORMATION_SCHEMA.COLUMNS
                    WHERE TABLE_NAME = 'AspNetUsers' AND COLUMN_NAME = '{column}'
                    """));
        }
    }

    [Fact]
    public async Task The_email_index_is_unique_and_filtered_so_nulls_do_not_collide()
    {
        // Identity's own default here is a NON-unique index; making it unique is this feature's
        // deliberate change (data-model.md § Indexes). Filtered, because NormalizedEmail stays
        // nullable and SQL Server would otherwise treat two NULLs as a collision.
        Assert.Equal(1, await IndexFlagAsync("AspNetUsers", "EmailIndex", "is_unique"));
        Assert.Equal(1, await IndexFlagAsync("AspNetUsers", "EmailIndex", "has_filter"));
    }

    [Fact]
    public async Task The_display_name_index_is_unique_and_needs_no_filter()
    {
        Assert.Equal(1, await IndexFlagAsync("AspNetUsers", "DisplayNameIndex", "is_unique"));
        Assert.Equal(0, await IndexFlagAsync("AspNetUsers", "DisplayNameIndex", "has_filter"));
    }

    [Fact]
    public async Task A_second_account_on_an_already_registered_address_is_refused_by_the_database()
    {
        var address = $"{Guid.NewGuid():N}@example.test";
        await factory.ExecuteAsync(
            SchemaQueries.InsertAccountSql(Guid.CreateVersion7(), address, $"first-{Guid.NewGuid():N}"));

        var collision = await Assert.ThrowsAsync<SqlException>(() => factory.ExecuteAsync(
            SchemaQueries.InsertAccountSql(Guid.CreateVersion7(), address, $"second-{Guid.NewGuid():N}")));

        Assert.Contains("EmailIndex", collision.Message);
    }

    [Fact]
    public async Task A_second_account_on_an_already_used_display_name_is_refused_by_the_database()
    {
        var displayName = $"taken-{Guid.NewGuid():N}";
        await factory.ExecuteAsync(SchemaQueries.InsertAccountSql(
            Guid.CreateVersion7(), $"{Guid.NewGuid():N}@example.test", displayName));

        var collision = await Assert.ThrowsAsync<SqlException>(() => factory.ExecuteAsync(
            SchemaQueries.InsertAccountSql(
                Guid.CreateVersion7(), $"{Guid.NewGuid():N}@example.test", displayName)));

        Assert.Contains("DisplayNameIndex", collision.Message);
    }

    [Fact]
    public async Task Two_accounts_without_an_address_do_not_collide_with_each_other()
    {
        // The filter is what makes this legal: Identity permits a null NormalizedEmail, and two
        // unknown addresses are not the same address.
        await factory.ExecuteAsync(
            SchemaQueries.InsertAccountSql(Guid.CreateVersion7(), null, $"anon-a-{Guid.NewGuid():N}"));
        await factory.ExecuteAsync(
            SchemaQueries.InsertAccountSql(Guid.CreateVersion7(), null, $"anon-b-{Guid.NewGuid():N}"));
    }

    private Task<int> IndexFlagAsync(string table, string index, string flag) =>
        factory.ScalarAsync<int>(
            $"""
            SELECT CAST({flag} AS int) FROM sys.indexes
            WHERE name = N'{index}' AND object_id = OBJECT_ID(N'[dbo].[{table}]')
            """);
}
