using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Infrastructure;

namespace Uniqua.Projector.Api.IntegrationTests;

/// <summary>
/// The skeleton's anchor test. It is deliberately not a feature test: it asserts only that the
/// application boots through the real harness, against the real store, and answers. Every feature
/// test added later inherits this harness, so if this one goes red nothing below it is trustworthy.
/// </summary>
public sealed class SkeletonSmokeTests(ApiFactory factory) : IClassFixture<ApiFactory>
{
    [Fact]
    public async Task Health_endpoint_answers_through_the_booted_application()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/health");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task An_unhandled_failure_is_rendered_as_problem_details()
    {
        var client = factory.CreateClient();

        var response = await client.GetAsync("/boom");

        Assert.Equal(System.Net.HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }

    [Fact]
    public async Task The_migrated_container_database_is_reachable_from_the_booted_application()
    {
        using var scope = factory.Services.CreateScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database;

        Assert.True(await database.CanConnectAsync());
        Assert.Empty(await database.GetPendingMigrationsAsync());
    }
}
