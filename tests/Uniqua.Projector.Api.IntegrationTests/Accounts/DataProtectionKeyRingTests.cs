using Microsoft.AspNetCore.DataProtection;
using Microsoft.Extensions.Options;
using Uniqua.Projector.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T5 — ADR 0009's key ring. spec §6 and KPI 3 commit to every unexpired session surviving a
/// redeploy, and the framework's default key ring lives in a per-process folder, so the first
/// container replacement would silently end every session. Keeping the ring in the database is
/// what makes that promise true — and this suite proves it without performing a deployment, by
/// building a second application over the same database and asking it to read the first one's work.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class DataProtectionKeyRingTests(ApiFactory factory)
{
    [Fact]
    public async Task The_key_ring_table_exists_with_the_three_columns_the_framework_writes()
    {
        Assert.Equal(1, await factory.TableCountAsync("DataProtectionKeys"));

        var columns = await factory.ScalarAsync<string>(
            """
            SELECT STRING_AGG(COLUMN_NAME, ',') WITHIN GROUP (ORDER BY COLUMN_NAME)
            FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = 'DataProtectionKeys'
            """);

        Assert.Equal("FriendlyName,Id,Xml", columns);
    }

    [Fact]
    public async Task The_key_ring_key_is_an_identity_int_because_the_framework_chose_it()
    {
        // A deliberate divergence from this repository's GUID v7 rule: the shape of this table is
        // the framework's, not ours, and it is recorded in the data-model audit report.
        Assert.Equal("int", await factory.ScalarAsync<string>(
            """
            SELECT DATA_TYPE FROM INFORMATION_SCHEMA.COLUMNS
            WHERE TABLE_NAME = 'DataProtectionKeys' AND COLUMN_NAME = 'Id'
            """));
        Assert.Equal(1, await factory.ScalarAsync<int>(
            """
            SELECT CAST(COLUMNPROPERTY(OBJECT_ID(N'[dbo].[DataProtectionKeys]'), N'Id', 'IsIdentity') AS int)
            """));
    }

    [Fact]
    public async Task No_index_beyond_the_primary_key_is_created()
    {
        // The framework reads the whole table at startup and it holds single-digit rows, so a
        // second index would be weight with no query behind it.
        Assert.Equal(1, await factory.ScalarAsync<int>(
            """
            SELECT COUNT(*) FROM sys.indexes
            WHERE object_id = OBJECT_ID(N'[dbo].[DataProtectionKeys]') AND name IS NOT NULL
            """));
    }

    [Fact]
    public void The_running_application_protects_with_the_ring_in_the_database()
    {
        var protector = factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("session-cookie");

        var protectedPayload = protector.Protect("a-session-reference");

        Assert.Equal("a-session-reference", protector.Unprotect(protectedPayload));
    }

    [Fact]
    public async Task A_second_instance_over_the_same_database_reads_the_first_ones_key_ring()
    {
        // This is the redeploy, staged: a wholly separate application, sharing nothing but the
        // database, unprotecting what the running one produced. If the ring were per-process this
        // would throw, which is exactly what would happen to every session on a container swap.
        var protectedPayload = factory.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("session-cookie")
            .Protect("a-session-reference");

        await using var replacement = new ApiFactory.Replacement(factory.ConnectionString);

        var unprotected = replacement.Services
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector("session-cookie")
            .Unprotect(protectedPayload);

        Assert.Equal("a-session-reference", unprotected);
    }

    [Fact]
    public void The_data_protection_application_name_is_set_explicitly()
    {
        // The ring is scoped by the application name. Inheriting it from the content root means a
        // rename — or a different working directory in a container — orphans the ring and ends
        // every session, so it is pinned to a constant instead.
        var discriminator = factory.Services
            .GetRequiredService<IOptions<DataProtectionOptions>>()
            .Value.ApplicationDiscriminator;

        Assert.Equal(Infrastructure.DependencyInjection.DataProtectionApplicationName, discriminator);
        Assert.False(string.IsNullOrWhiteSpace(discriminator));
    }
}
