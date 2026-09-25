using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Uniqua.Projector.Application.Accounts;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Application.Boards;

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
        services.AddScoped<SignIn>();
        services.AddScoped<SignOut>();

        services.AddScoped<CreateBoard>();
        services.AddScoped<ListMyBoards>();
        services.AddScoped<OpenBoard>();
        services.AddScoped<RenameBoard>();
        services.AddScoped<DeleteBoard>();

        services.AddScoped<AddColumn>();
        services.AddScoped<RenameColumn>();
        services.AddScoped<MoveColumn>();
        services.AddScoped<DeleteColumn>();

        services.AddScoped<AddCard>();
        services.AddScoped<OpenCard>();
        services.AddScoped<EditCard>();
        services.AddScoped<DeleteCard>();

        // The announcement has no listener until the live-update channel arrives (roadmap step 8).
        // Registered with TryAdd so the Api-side implementation beside the hub replaces it simply
        // by being registered first, rather than by editing this line.
        services.TryAddScoped<ISessionRevocationNotifier, NoSessionRevocationNotifier>();

        return services;
    }
}
