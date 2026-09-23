using Microsoft.AspNetCore.Antiforgery;

namespace Uniqua.Projector.Api.Antiforgery;

/// <summary>
/// The forgery guard. The session is carried by a cookie the browser attaches on its own, so the
/// cookie alone is never proof that the account meant to make the request — a page on another site
/// could otherwise act as a signed-in account (spec §6.1). Every state-changing request therefore
/// has to carry a token this application issued.
/// </summary>
/// <remarks>
/// <para>
/// <strong>OQ-API-1 — the chosen token-acquisition shape.</strong> sad.md §8 requires the token and
/// contracts/openapi.yaml declares <c>X-XSRF-TOKEN</c> required on the three state-changing
/// operations, but no §6 flow and no §5 acceptance criterion said how the client obtains one. The
/// shape implemented here is the framework's standard cookie-and-header pair, chosen because it
/// needs no new endpoint and so changes no published operation:
/// </para>
/// <list type="number">
/// <item>every safe (read) response issues a readable <c>XSRF-TOKEN</c> cookie;</item>
/// <item>the client reads that cookie and echoes the value in the <c>X-XSRF-TOKEN</c> header;</item>
/// <item>the header is validated against the paired httpOnly cookie the framework also sets.</item>
/// </list>
/// <para>
/// The <c>XSRF-TOKEN</c> cookie is deliberately <em>not</em> httpOnly — the client has to read it —
/// which is safe because it is worthless without the httpOnly half. The shape is now declared in
/// contracts/openapi.yaml (the <c>getCurrentAccount</c> Set-Cookie header and the
/// <c>components.parameters.AntiforgeryToken</c> description), and OQ-API-1 is closed (review
/// 2026-09-23, fourth re-review, U-03).
/// </para>
/// </remarks>
public static class AntiforgerySetup
{
    /// <summary>The cookie the client reads the request token out of.</summary>
    public const string ClientReadableCookieName = "XSRF-TOKEN";

    /// <summary>The header the client echoes it back in, as the contract declares.</summary>
    public const string HeaderName = "X-XSRF-TOKEN";

    public static IServiceCollection AddAntiforgeryGuard(this IServiceCollection services)
    {
        services.AddAntiforgery(options =>
        {
            options.HeaderName = HeaderName;
            // The framework's own half stays httpOnly and same-site, like the session cookie.
            options.Cookie.Name = "__Host-projector-antiforgery";
            options.Cookie.HttpOnly = true;
            options.Cookie.SameSite = SameSiteMode.Strict;
            options.Cookie.SecurePolicy = CookieSecurePolicy.Always;
        });

        return services;
    }

    /// <summary>
    /// Guards every state-changing request under the API, rather than relying on each endpoint to
    /// remember. Registered as middleware and not as per-endpoint metadata precisely because the
    /// failure mode ADR 0007 flagged is an endpoint that quietly forgets to ask.
    /// </summary>
    public static IApplicationBuilder UseAntiforgeryGuard(this WebApplication app)
    {
        var antiforgery = app.Services.GetRequiredService<IAntiforgery>();

        return app.Use(async (context, next) =>
        {
            if (IsStateChanging(context.Request.Method))
            {
                try
                {
                    await antiforgery.ValidateRequestAsync(context);
                }
                catch (AntiforgeryValidationException)
                {
                    // Nothing about the session is touched: a forgery attempt must not become a
                    // way of signing its target out.
                    await context.WriteAccountProblemAsync("accounts.antiforgery_failed");
                    return;
                }
            }
            else
            {
                // A read is never asked for a token, and is what hands the client the one it will
                // need in order to write (step 1 of the shape above).
                IssueClientReadableToken(context, antiforgery);
            }

            await next(context);
        });
    }

    private static void IssueClientReadableToken(HttpContext context, IAntiforgery antiforgery)
    {
        var tokens = antiforgery.GetAndStoreTokens(context);
        if (tokens.RequestToken is null)
        {
            return;
        }

        context.Response.Cookies.Append(ClientReadableCookieName, tokens.RequestToken, new CookieOptions
        {
            HttpOnly = false, // the client has to read it; it is useless without the httpOnly half
            SameSite = SameSiteMode.Strict,
            Secure = true,
            IsEssential = true,
        });
    }

    private static bool IsStateChanging(string method) =>
        !HttpMethods.IsGet(method)
        && !HttpMethods.IsHead(method)
        && !HttpMethods.IsOptions(method)
        && !HttpMethods.IsTrace(method);
}
