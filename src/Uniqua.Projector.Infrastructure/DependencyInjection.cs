using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain.Accounts;
using Uniqua.Projector.Infrastructure.Accounts;

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
        services.AddDbContext<AppDbContext>((provider, options) =>
        {
            options.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName));

            // Any IInterceptor registered in the container is attached here. This is the one place
            // the context is configured, so it is the one place an interceptor can be added
            // without a second copy of the connection wiring drifting away from this one.
            options.AddInterceptors(provider.GetServices<IInterceptor>());
        });

        // ADR 0009: the key ring goes in the store that already exists, so a redeploy carries it
        // and the database backup covers it with no second artefact to remember.
        services.AddDataProtection()
            .SetApplicationName(DataProtectionApplicationName)
            .PersistKeysToDbContext<AppDbContext>();

        AddAccounts(services);

        return services;
    }

    private static void AddAccounts(IServiceCollection services)
    {
        services.AddIdentityCore<ProjectorUser>(options =>
        {
            // ADR 0010. The framework's lockout would make an account unusable to its owner, which
            // AC-12 forbids; the failure count it maintains is kept and reused as data for the
            // delay curve instead. This switch is the whole of that decision, and the schema
            // cannot enforce it — only IdentityAccountStoreTests can.
            options.Lockout.AllowedForNewUsers = false;

            // The address is Identity's login key and must identify exactly one account (AC-03).
            options.User.RequireUniqueEmail = true;

            // The password bounds belong to the Account entity (AC-02), so Identity's own
            // validators are stood down rather than duplicating — and disagreeing with — them.
            options.Password.RequiredLength = Account.MinPasswordLength;
            options.Password.RequireDigit = false;
            options.Password.RequireLowercase = false;
            options.Password.RequireUppercase = false;
            options.Password.RequireNonAlphanumeric = false;
            options.Password.RequiredUniqueChars = 1;
        })
        .AddEntityFrameworkStores<AppDbContext>();

        services.Configure<PasswordHasherOptions>(options =>
        {
            // spec §6: at least 100 ms per verification. The figure is Domain's, held by
            // PasswordHashingCostTests, so lowering it means re-measuring rather than editing a
            // number here.
            options.CompatibilityMode = PasswordHasherCompatibilityMode.IdentityV3;
            options.IterationCount = PasswordHashingCost.IterationCount;
        });

        services.AddSingleton<DummyCredential>();
        services.AddSingleton<IClock, SystemClock>();
        services.AddScoped<IAccountStore, IdentityAccountStore>();

        // A singleton because it is the state: a guesser's attempts arrive on separate requests.
        services.AddSingleton<IUnknownAddressAttempts, InMemoryUnknownAddressAttempts>();

        // One implementation, registered against both ports: they are two views of one table, and
        // keeping them separate interfaces is what stops the recognition path acquiring the write
        // path's needs.
        services.AddScoped<IUnitOfWork, UnitOfWork>();

        services.AddScoped<SessionStore>();
        services.AddScoped<ISessionReader>(services => services.GetRequiredService<SessionStore>());
        services.AddScoped<ISessionStore>(services => services.GetRequiredService<SessionStore>());
    }
}
