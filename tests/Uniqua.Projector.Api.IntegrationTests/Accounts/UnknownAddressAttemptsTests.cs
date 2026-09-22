using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T22 — the count behind AC-05b for addresses no account owns. It must count exactly as a
/// registered account's does, or the difference in the wait becomes the oracle AC-05b rules out.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class UnknownAddressAttemptsTests(ApiFactory factory)
{
    private static readonly DateTimeOffset Start = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);

    private IUnknownAddressAttempts Attempts =>
        factory.Services.GetRequiredService<IUnknownAddressAttempts>();

    [Fact]
    public void One_address_typed_two_ways_is_one_count()
    {
        var address = $"{Guid.NewGuid():N}@example.test";

        Assert.Equal(1, Attempts.RecordFailure(address, Start));
        Assert.Equal(2, Attempts.RecordFailure($"  {address.ToUpperInvariant()} ", Start));
    }

    [Fact]
    public void Different_addresses_are_counted_apart()
    {
        Assert.Equal(1, Attempts.RecordFailure($"{Guid.NewGuid():N}@example.test", Start));
        Assert.Equal(1, Attempts.RecordFailure($"{Guid.NewGuid():N}@example.test", Start));
    }

    [Fact]
    public void Fifteen_quiet_minutes_start_the_count_again()
    {
        var address = $"{Guid.NewGuid():N}@example.test";

        for (var attempt = 0; attempt < 7; attempt++)
        {
            Attempts.RecordFailure(address, Start);
        }

        Assert.Equal(1, Attempts.RecordFailure(address, Start + GuessingDelay.ResetAfter));
    }
}
