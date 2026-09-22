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
    public static IServiceCollection AddInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<AppDbContext>(options =>
            options.UseSqlServer(
                configuration.GetConnectionString("Default"),
                sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName)));

        return services;
    }
}
