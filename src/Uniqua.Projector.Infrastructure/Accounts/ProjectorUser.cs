using Microsoft.AspNetCore.Identity;

namespace Uniqua.Projector.Infrastructure.Accounts;

/// <summary>
/// The persistence shape of an account: ASP.NET Core Identity's user, keyed by a GUID because
/// identifiers in this repository are application-generated GUID v7 (architecture-map
/// § Conventions), plus the three columns this feature adds.
/// </summary>
/// <remarks>
/// This type lives in Infrastructure deliberately. It is a store record, not the domain entity —
/// the invariants an account has to keep live in <c>Uniqua.Projector.Domain.Accounts.Account</c>,
/// which knows nothing about Identity or EF Core.
/// </remarks>
public sealed class ProjectorUser : IdentityUser<Guid>
{
    /// <summary>What other members see. AC-11: never the email address.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// The comparison key behind AC-11b. Produced in the application by Identity's normalizer
    /// (trim, then invariant upper-case), so whether two names collide is decided by code that
    /// travels with the application rather than by the database's collation.
    /// </summary>
    public string NormalizedDisplayName { get; set; } = string.Empty;

    /// <summary>
    /// When the most recent failed sign-in happened. AC-12's 15-minute reset is derived from this
    /// together with <see cref="IdentityUser{TKey}.AccessFailedCount"/>; nothing schedules a job.
    /// </summary>
    public DateTimeOffset? LastFailedAttemptAt { get; set; }
}
