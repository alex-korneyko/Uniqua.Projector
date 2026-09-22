using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Uniqua.Projector.Domain.Accounts;
using Uniqua.Projector.Infrastructure.Accounts;

namespace Uniqua.Projector.Infrastructure;

/// <summary>
/// The single EF Core context. It lives in Infrastructure and is reachable from nowhere else —
/// Domain does not know it exists and Api only registers it (see docs/architecture-map.md
/// § Conventions: Persistence / DB access).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ProjectorUser, IdentityRole<Guid>, Guid>(options), IDataProtectionKeyContext
{
    /// <summary>The session records ADR 0008 chose over self-contained cookie tickets.</summary>
    public DbSet<Session> Sessions => Set<Session>();

    /// <summary>
    /// The data-protection key ring (ADR 0009). It lives in the database so a container
    /// replacement carries it along and every unexpired session is still recognised — the
    /// framework's default keeps it in a per-process folder, which would end every session on the
    /// first redeploy. The shape of this table is the framework's, not ours.
    /// </summary>
    public DbSet<DataProtectionKey> DataProtectionKeys => Set<DataProtectionKey>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
