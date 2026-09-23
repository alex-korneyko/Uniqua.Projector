using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Uniqua.Projector.Api.IntegrationTests.Fixtures;

/// <summary>
/// Gives a test request its own apparent TCP peer. The test server invents no connection address,
/// so every request would otherwise arrive from the same nowhere — and AC-01b's per-source limit
/// is a singleton, which would make the whole suite share one counter and each test's result
/// depend on which ran before it.
/// </summary>
/// <remarks>
/// This lives in the test assembly and is registered only by the test host. The application under
/// test has no such hook: nothing in <c>src/</c> lets a caller choose its own connection address,
/// which is exactly the property <c>A_forwarded_address_from_an_untrusted_caller_is_ignored</c>
/// exists to check. What this simulates is a different TCP peer, not a client's claim about
/// itself — the two are different things, and only the first is legitimately trusted.
/// </remarks>
public sealed class TestPeerAddress : IStartupFilter
{
    /// <summary>The test-only header naming the peer a request should appear to come from.</summary>
    public const string HeaderName = "X-Test-Peer";

    // Q-13(c): 16 random bits from Guid.NewGuid() collide well inside the size of a full suite run
    // (a birthday-bound problem at a few hundred draws from a 65536-address space), and
    // TestClock.Reset() only rewinds the clock, not any per-source counter a rate limiter keeps —
    // so a peer that collided with an already-exhausted one earlier in the run stays exhausted for
    // every test after it, for no reason a single test can see. A monotonic counter can never repeat
    // an address within a run, so no two tests can ever be handed the same peer.
    private static long _next = -1;

    /// <summary>A fresh address, never handed out before in this run, so one test's registrations
    /// never count against another's.</summary>
    public static string Fresh()
    {
        // 198.18.0.0/15 is reserved for benchmarking, so these can never be a real client. The
        // range holds 2^17 addresses (198.18.0.0-198.19.255.255); the counter is taken modulo that
        // so it can never walk outside it even after an implausibly long run.
        var offset = (uint)(Interlocked.Increment(ref _next) & 0x1FFFF);
        var secondOctet = (byte)(18 + (offset >> 16));
        var thirdOctet = (byte)(offset >> 8);
        var fourthOctet = (byte)offset;

        return new IPAddress([198, secondOctet, thirdOctet, fourthOctet]).ToString();
    }

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.Use(async (context, proceed) =>
        {
            if (context.Request.Headers.TryGetValue(HeaderName, out var peer)
                && IPAddress.TryParse(peer.ToString(), out var address))
            {
                context.Connection.RemoteIpAddress = address;
            }

            await proceed(context);
        });

        next(app);
    };
}
