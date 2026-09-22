using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Infrastructure;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// Every migration this feature adds has to go forward onto an empty database and come back to
/// nothing. A migration that only applies is a one-way door: the first deployment that has to be
/// rolled back discovers the down leg was never run. This runs the whole chain on a scratch
/// database of its own so it cannot disturb the suite's shared schema.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class MigrationRoundTripTests(ApiFactory factory)
{
    [Fact]
    public async Task The_whole_chain_applies_to_an_empty_database_and_reverts_to_nothing()
    {
        var scratch = $"roundtrip_{Guid.NewGuid():N}";
        await factory.ExecuteAsync($"CREATE DATABASE [{scratch}];");

        try
        {
            await using var context = ScratchContext(factory.ConnectionString, scratch);
            var migrator = context.GetService<IMigrator>();

            await context.Database.MigrateAsync();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
            Assert.Equal(1, await TableCountAsync(factory.ConnectionString, scratch, "AspNetUsers"));

            // Target "0" is EF Core's name for "before the first migration".
            await migrator.MigrateAsync("0");
            Assert.Equal(0, await TableCountAsync(factory.ConnectionString, scratch, "AspNetUsers"));
        }
        finally
        {
            await factory.ExecuteAsync(
                $"ALTER DATABASE [{scratch}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{scratch}];");
        }
    }

    private static AppDbContext ScratchContext(string connectionString, string database)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database };
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer(
                builder.ConnectionString,
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
            .Options;

        return new AppDbContext(options);
    }

    private static async Task<int> TableCountAsync(string connectionString, string database, string table)
    {
        var builder = new SqlConnectionStringBuilder(connectionString) { InitialCatalog = database };
        await using var connection = new SqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(
            $"SELECT COUNT(*) FROM sys.tables WHERE name = N'{table}'", connection);
        return (int)(await command.ExecuteScalarAsync())!;
    }
}
