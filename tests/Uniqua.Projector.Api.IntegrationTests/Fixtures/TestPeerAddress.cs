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

    /// <summary>A fresh address, so one test's registrations never count against another's.</summary>
    public static string Fresh()
    {
        var octets = Guid.NewGuid().ToByteArray();

        // 198.18.0.0/15 is reserved for benchmarking, so these can never be a real client.
        return new IPAddress([198, 18, octets[0], octets[1]]).ToString();
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
