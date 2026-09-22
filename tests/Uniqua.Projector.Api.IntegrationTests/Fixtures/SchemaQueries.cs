using Microsoft.Data.SqlClient;

namespace Uniqua.Projector.Api.IntegrationTests.Fixtures;

/// <summary>
/// Reads the physical schema of the migrated database and runs raw statements against it. A
/// migration task's promise is about the shape of the store, so these tests interrogate SQL
/// Server's own catalogue rather than the EF Core model that produced it — a model assertion
/// would pass even if the migration never emitted the index.
/// </summary>
public static class SchemaQueries
{
    public static async Task<T?> ScalarAsync<T>(this ApiFactory factory, string sql)
    {
        await using var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        var value = await command.ExecuteScalarAsync();
        return value is null or DBNull ? default : (T)Convert.ChangeType(value, typeof(T));
    }

    public static async Task ExecuteAsync(this ApiFactory factory, string sql)
    {
        await using var connection = new SqlConnection(factory.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>The number of dbo tables carrying this name — 1 when the migration created it.</summary>
    public static Task<int> TableCountAsync(this ApiFactory factory, string table) =>
        factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM sys.tables WHERE name = N'{table}' AND SCHEMA_NAME(schema_id) = N'dbo'");

    /// <summary>Inserts a minimally valid AspNetUsers row, filling every NOT NULL column.</summary>
    public static string InsertAccountSql(Guid id, string? normalizedEmail, string normalizedDisplayName)
    {
        var userName = normalizedEmail ?? Guid.NewGuid().ToString();
        var email = normalizedEmail is null ? "NULL" : $"'{normalizedEmail}'";

        return $"""
            INSERT INTO [dbo].[AspNetUsers]
                ([Id], [UserName], [NormalizedUserName], [Email], [NormalizedEmail], [EmailConfirmed],
                 [PhoneNumberConfirmed], [TwoFactorEnabled], [LockoutEnabled], [AccessFailedCount],
                 [DisplayName], [NormalizedDisplayName])
            VALUES
                ('{id}', '{userName}', '{userName}', {email}, {email},
                 0, 0, 0, 0, 0, '{normalizedDisplayName}', '{normalizedDisplayName}');
            """;
    }
}
