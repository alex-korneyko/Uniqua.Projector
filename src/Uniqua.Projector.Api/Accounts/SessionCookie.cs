using Microsoft.AspNetCore.DataProtection;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Api.Accounts;

/// <summary>
/// The session cookie: its name, its attributes, and the protection around the reference it
/// carries — in one place, because three endpoints and one authentication handler all depend on
/// getting them identical, and an attribute set in four places is an attribute wrong in one.
/// </summary>
/// <remarks>
/// The cookie carries an <em>opaque</em> reference rather than the session id itself. That matters
/// for two reasons: what the browser holds is not usable as a database key by anything that sees
/// it, and the protection is what ties a cookie's validity to the key ring in the database (ADR
/// 0009), so a redeploy keeps every unexpired session instead of quietly ending them all.
/// </remarks>
public static class SessionCookie
{
    /// <summary>As the contract states it.</summary>
    public const string Name = "projector_session";

    /// <summary>
    /// The Data Protection purpose the reference is protected under. Public so a test can build
    /// the same cookie the application would.
    /// </summary>
    public const string ProtectorPurpose = "Uniqua.Projector.Accounts.SessionCookie";

    /// <summary>Sets the cookie for a session.</summary>
    public static void Issue(HttpContext context, Guid sessionId)
    {
        var attributes = Attributes(context);

        // AC-06: a cookie with no lifetime is a browser-session cookie and dies when the browser
        // closes. It is given the longest life a session can have; whether the session is still
        // good is decided on every request by the server-side rules, never by the cookie.
        attributes.MaxAge = Session.AbsoluteLifetime;

        context.Response.Cookies.Append(
            Name,
            Protector(context).Protect(sessionId.ToString()),
            attributes);
    }

    /// <summary>
    /// Clears the cookie. The attributes have to match the ones it was set with, or the browser
    /// keeps the original and a sign-out appears not to have worked.
    /// </summary>
    public static void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(Name, Attributes(context));

    /// <summary>
    /// The session this request claims to be, or <c>null</c>.
    /// </summary>
    /// <remarks>
    /// A value that cannot be unprotected — forged, tampered with, or signed by a key no longer in
    /// the ring — returns null rather than throwing, and so is refused in exactly the same words
    /// as a session that was never issued. That indistinguishability is the point: a forged cookie
    /// must teach its author nothing.
    /// </remarks>
    public static Guid? Read(HttpContext context)
    {
        if (context.Request.Cookies[Name] is not { Length: > 0 } cookieValue)
        {
            return null;
        }

        try
        {
            return Guid.TryParse(Protector(context).Unprotect(cookieValue), out var sessionId)
                ? sessionId
                : null;
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return null;
        }
    }

    private static CookieOptions Attributes(HttpContext context) => new()
    {
        // No page script may read it: a cross-site scripting bug elsewhere in the product must not
        // become a way of stealing sessions (spec §6.1).
        HttpOnly = true,
        // Lax rather than Strict, as the contract states: a session has to survive arriving at the
        // product from a link, which Strict would break.
        SameSite = SameSiteMode.Lax,
        Secure = true,
        Path = "/",
        IsEssential = true,
    };

    private static IDataProtector Protector(HttpContext context) =>
        context.RequestServices
            .GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(ProtectorPurpose);
}
