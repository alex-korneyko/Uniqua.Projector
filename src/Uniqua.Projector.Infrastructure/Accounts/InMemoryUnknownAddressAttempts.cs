using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Uniqua.Projector.Application.Accounts.Ports;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Infrastructure.Accounts;

/// <summary>
/// <see cref="IUnknownAddressAttempts"/> held in process memory, keyed by a hash of the normalised
/// address. One instance serves the whole process, since a guesser's attempts arrive on different
/// requests.
/// </summary>
/// <remarks>
/// Two accepted limits. A restart clears every count, so for five attempts after one an
/// unregistered address answers faster than a registered one that was already under guessing;
/// and past <see cref="Capacity"/> distinct addresses inside one reset window, new addresses are
/// not tracked at all. Both only matter to someone spraying addresses at a scale the
/// registration-form enumeration spec §6.1 already accepts would serve them better.
/// </remarks>
internal sealed class InMemoryUnknownAddressAttempts : IUnknownAddressAttempts
{
    /// <summary>The most addresses tracked at once, so a spray of addresses cannot exhaust memory.</summary>
    internal const int Capacity = 100_000;

    /// <summary>How many recordings pass between sweeps for counts the reset has already cleared.</summary>
    private const int PruneEvery = 1_024;

    private readonly ConcurrentDictionary<string, Attempts> _attempts = new(StringComparer.Ordinal);

    // The normalizer IdentityAccountStore.FindByEmailAsync reaches through UserManager, so one
    // address typed two ways is one count here exactly as it is one account there.
    private readonly UpperInvariantLookupNormalizer _normalizer = new();

    private int _sinceLastPrune;

    public int RecordFailure(string email, DateTimeOffset now)
    {
        if (Interlocked.Increment(ref _sinceLastPrune) % PruneEvery == 0)
        {
            Prune(now);
        }

        var key = Key(email);

        if (_attempts.Count >= Capacity && !_attempts.ContainsKey(key))
        {
            return 1;
        }

        return _attempts.AddOrUpdate(
            key,
            _ => new Attempts(1, now),
            (_, stored) => new Attempts(
                GuessingDelay.CountAfterFailure(stored.Count, stored.LastAt, now), now))
            .Count;
    }

    private void Prune(DateTimeOffset now)
    {
        foreach (var entry in _attempts)
        {
            if (now - entry.Value.LastAt >= GuessingDelay.ResetAfter)
            {
                // Removes only the value that was read, so a failure recorded meanwhile survives.
                _attempts.TryRemove(entry);
            }
        }
    }

    private string Key(string email)
    {
        var normalized = _normalizer.NormalizeEmail(email.Trim()) ?? string.Empty;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private sealed record Attempts(int Count, DateTimeOffset LastAt);
}
