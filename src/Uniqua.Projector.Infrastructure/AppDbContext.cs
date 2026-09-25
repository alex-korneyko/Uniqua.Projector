using Microsoft.AspNetCore.DataProtection.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Uniqua.Projector.Domain.Accounts;
using Uniqua.Projector.Domain.Boards;
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

    /// <summary>
    /// The board aggregate roots (ADR 0015). Columns and memberships are reached only through their
    /// board; cards are read and written one at a time, but admitted only through the board.
    /// </summary>
    public DbSet<Board> Boards => Set<Board>();

    public DbSet<Card> Cards => Set<Card>();

    /// <summary>One row per account that has ever created a board — the 50-board ceiling's guard.</summary>
    public DbSet<OwnedBoardCounter> OwnedBoardCounters => Set<OwnedBoardCounter>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
