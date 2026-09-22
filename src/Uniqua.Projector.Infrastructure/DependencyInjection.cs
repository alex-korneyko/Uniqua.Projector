using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Uniqua.Projector.Infrastructure;

/// <summary>
/// The Infrastructure layer's single registration surface. The repository implementations that
/// satisfy Application's ports are bound here; nothing outside this file names a concrete adapter.
/// </summary>
public static class DependencyInjection
{
    /// <summary>
    /// The name the data-protection key ring is scoped by. Pinned to a constant rather than
    /// inherited from the content root: a rename, or simply a different working directory inside a
    /// container, would otherwise orphan the ring and end every session at once.
    /// </summary>
    public const string DataProtectionApplicationName = "Uniqua.Projector";

    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        // ADR 0009: the key ring goes in the store that already exists, so a redeploy carries it
        // and the database backup covers it with no second artefact to remember.
        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<AppDbContext>();

        return services;
    }
}
