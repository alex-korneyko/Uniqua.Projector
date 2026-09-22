using Microsoft.Extensions.DependencyInjection;
using Uniqua.Projector.Api.IntegrationTests.Fixtures;
using Uniqua.Projector.Application.Accounts;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.IntegrationTests.Accounts;

/// <summary>
/// T10 — signing out. AC-08 is as much about what does not happen as what does: the session the
/// request arrived on ends, and any session the same account holds elsewhere is left alone. AC-09
/// adds that the revocation is announced, which is all it can be held to until the live-update
/// channel arrives at roadmap step 8.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class SignOutTests(ApiFactory factory)
{
    /// <summary>
    /// A notifier that records rather than announces, and can be told to fail. It also captures
    /// whether the store had already been written when it was called, which is the ordering AC-09
    /// depends on — announcing a revocation that is not yet a fact would let a listener act on
    /// something that could still be rolled back.
    /// </summary>
    private sealed class RecordingNotifier(Func<Guid, Task<bool>> wasAlreadyRevoked)
        : ISessionRevocationNotifier
    {
        private readonly List<Guid> _announced = [];

        public IReadOnlyList<Guid> Announced => _announced;

        public bool StoreWasWrittenFirst { get; private set; }

        public bool Throw { get; set; }

        public async Task SessionRevokedAsync(Guid sessionId, CancellationToken cancellationToken)
        {
            StoreWasWrittenFirst = await wasAlreadyRevoked(sessionId);
            _announced.Add(sessionId);

            if (Throw)
            {
                throw new InvalidOperationException("the hub is unavailable");
            }
        }
    }

    // ---- AC-09: announced exactly once, after the write ----------------------------------------

    [Fact]
    public async Task Signing_out_announces_the_revocation_exactly_once_after_it_is_a_fact()
    {
        factory.Clock.Reset();
        var session = await AnOpenSessionAsync();
        var notifier = NewNotifier();

        await SignOutWith(notifier, session.Id);

        Assert.Equal([session.Id], notifier.Announced);
        Assert.True(
            notifier.StoreWasWrittenFirst,
            "the revocation was announced before it was written, so a listener could act on "
            + "something that had not happened yet");
    }

    [Fact]
    public async Task A_notifier_that_fails_does_not_undo_a_completed_sign_out()
    {
        // The revocation is already written. Turning a completed sign-out into an error because
        // the hub was unreachable would leave the caller believing they are still signed in while
        // their session is in fact over.
        factory.Clock.Reset();
        var session = await AnOpenSessionAsync();
        var notifier = NewNotifier();
        notifier.Throw = true;

        await SignOutWith(notifier, session.Id);

        Assert.True(await IsRevokedAsync(session.Id));
    }

    // ---- AC-08: this session, and only this one --------------------------------------------------

    [Fact]
    public async Task Signing_out_on_one_device_leaves_the_other_device_signed_in()
    {
        factory.Clock.Reset();
        var accountId = await AnAccountAsync();

        Session phone;
        Session laptop;
        using (var scope = factory.Services.CreateScope())
        {
            var store = scope.ServiceProvider.GetRequiredService<ISessionStore>();
            phone = await store.OpenAsync(accountId, CancellationToken.None);
            laptop = await store.OpenAsync(accountId, CancellationToken.None);
        }

        await SignOutWith(NewNotifier(), phone.Id);

        Assert.True(await IsRevokedAsync(phone.Id));
        Assert.False(await IsRevokedAsync(laptop.Id));
    }

    [Fact]
    public async Task Signing_out_twice_keeps_the_instant_the_session_actually_ended()
    {
        factory.Clock.Reset();
        var session = await AnOpenSessionAsync();
        var notifier = NewNotifier();

        await SignOutWith(notifier, session.Id);
        var ended = await RevokedAtAsync(session.Id);

        factory.Clock.Advance(TimeSpan.FromHours(2));
        await SignOutWith(notifier, session.Id);

        Assert.Equal(ended, await RevokedAtAsync(session.Id));
        factory.Clock.Reset();
    }

    [Fact]
    public async Task An_already_expired_session_is_revoked_anyway()
    {
        // Expiry and revocation are independent facts, so a sign-out arriving after the session
        // timed out still records that the account meant to end it.
        factory.Clock.Reset();
        var session = await AnOpenSessionAsync();
        factory.Clock.Advance(TimeSpan.FromDays(20));

        await SignOutWith(NewNotifier(), session.Id);

        Assert.True(await IsRevokedAsync(session.Id));
        factory.Clock.Reset();
    }

    [Fact]
    public async Task Signing_out_a_session_that_was_never_issued_is_not_an_error()
    {
        var notifier = NewNotifier();

        await SignOutWith(notifier, Guid.CreateVersion7());
    }

    // ---- The port keeps the hub out of Application ----------------------------------------------

    [Fact]
    public void The_notifier_port_names_no_hub_type_and_no_api_type()
    {
        // sad §5: signing out has to reach the connections a session holds, the hub lives in Api,
        // and §2 fixes the direction as Api → Application. The port is what keeps that legal.
        var assemblies = typeof(ISessionRevocationNotifier).GetMethods()
            .SelectMany(method => method.GetParameters()
                .Select(parameter => parameter.ParameterType)
                .Append(method.ReturnType))
            .Select(type => type.Assembly.GetName().Name ?? string.Empty)
            .Distinct();

        Assert.DoesNotContain("Microsoft.AspNetCore.SignalR.Core", assemblies);
        Assert.DoesNotContain("Uniqua.Projector.Api", assemblies);
    }

    // ---- Helpers --------------------------------------------------------------------------------

    private RecordingNotifier NewNotifier() => new(IsRevokedAsync);

    /// <summary>
    /// Builds the use case with the recording notifier in place of the registered one, so the
    /// ordering and the failure behaviour can be observed without a hub existing.
    /// </summary>
    private async Task SignOutWith(RecordingNotifier notifier, Guid sessionId)
    {
        using var scope = factory.Services.CreateScope();

        var signOut = new SignOut(
            scope.ServiceProvider.GetRequiredService<ISessionStore>(),
            notifier,
            scope.ServiceProvider.GetRequiredService<
                Microsoft.Extensions.Logging.ILogger<SignOut>>());

        await signOut.ExecuteAsync(sessionId, CancellationToken.None);
    }

    private async Task<bool> IsRevokedAsync(Guid sessionId) =>
        await factory.ScalarAsync<int>(
            $"SELECT COUNT(*) FROM [dbo].[Sessions] WHERE [Id] = '{sessionId}' AND [RevokedAt] IS NOT NULL") == 1;

    private Task<string?> RevokedAtAsync(Guid sessionId) =>
        factory.ScalarAsync<string>(
            $"""
            SELECT CONVERT(nvarchar(40), [RevokedAt], 127)
            FROM [dbo].[Sessions] WHERE [Id] = '{sessionId}'
            """);

    private async Task<Guid> AnAccountAsync()
    {
        var accountId = Guid.CreateVersion7();
        await factory.ExecuteAsync(SchemaQueries.InsertAccountSql(
            accountId, $"{Guid.NewGuid():N}@example.test", $"signout-{Guid.NewGuid():N}"));

        return accountId;
    }

    private async Task<Session> AnOpenSessionAsync()
    {
        var accountId = await AnAccountAsync();
        using var scope = factory.Services.CreateScope();

        return await scope.ServiceProvider
            .GetRequiredService<ISessionStore>()
            .OpenAsync(accountId, CancellationToken.None);
    }
}
