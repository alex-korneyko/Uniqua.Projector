using System.Collections.Concurrent;
using Uniqua.Projector.Application.Accounts.Ports;

namespace Uniqua.Projector.Api.Accounts;

/// <summary>
/// The Api-side half of <see cref="ISessionRevocationNotifier"/>. It sits here, next to where the
/// realtime hub will live, because sad §2 fixes the reference direction as Api → Application: a
/// use case may not call the hub, so the hub's side of the conversation is implemented on this end
/// of the port.
/// </summary>
/// <remarks>
/// <para>
/// <strong>The hub does not exist yet.</strong> The live-update channel arrives at roadmap step 8,
/// and <em>how</em> a revocation reaches an already-open connection is spec §8 open question 1 —
/// which the contract deliberately leaves unanswered, stating the outcome rather than the
/// mechanism. So what this does today is the whole of what AC-09 can be held to: it records the
/// revoked session and says so in the log, structurally, where step 8 will pick the thread up.
/// </para>
/// <para>
/// It is a singleton holding a bounded ring of recent revocations. Bounded on purpose: an
/// unbounded record of every session ever ended would be a slow leak in a long-running process,
/// and nothing today reads more than the most recent entries.
/// </para>
/// </remarks>
public sealed class HubSessionRevocationNotifier(ILogger<HubSessionRevocationNotifier> logger)
    : ISessionRevocationNotifier
{
    /// <summary>
    /// How many recent revocations are kept. Enough to be useful to a test or an operator looking
    /// at a single incident; far too few to be a store.
    /// </summary>
    private const int Remembered = 256;

    private readonly ConcurrentQueue<Guid> _recent = new();

    /// <summary>The sessions most recently announced, oldest first.</summary>
    public IReadOnlyCollection<Guid> Recent => [.. _recent];

    public Task SessionRevokedAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        _recent.Enqueue(sessionId);

        while (_recent.Count > Remembered && _recent.TryDequeue(out _))
        {
            // Drop the oldest; the ring is a window, not a ledger.
        }

        // A session reference is exactly what sad §8 keeps out of the log, so the count is
        // reported and the reference is not. When step 8 lands, this is the line that becomes a
        // send to the hub group for this session.
        logger.LogInformation(
            "module=accounts event=session_revocation_announced pending_channel=roadmap_step_8");

        return Task.CompletedTask;
    }

    /// <summary>Forgets the window. Used where a caller wants to observe one revocation in isolation.</summary>
    public void Forget()
    {
        while (_recent.TryDequeue(out _))
        {
        }
    }
}
