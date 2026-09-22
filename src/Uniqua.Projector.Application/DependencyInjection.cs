using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Accounts;

namespace Uniqua.Projector.Application;

/// <summary>
/// The Application layer's single registration surface. Every use case this layer adds is
/// registered here and nowhere else, so <c>Program.cs</c> never names an Application type.
/// </summary>
public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<RegisterAccount>();

        return services;
    }
}
