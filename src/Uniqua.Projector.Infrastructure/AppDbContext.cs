using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Uniqua.Projector.Infrastructure.Accounts;

namespace Uniqua.Projector.Infrastructure;

/// <summary>
/// The single EF Core context. It lives in Infrastructure and is reachable from nowhere else —
/// Domain does not know it exists and Api only registers it (see docs/architecture-map.md
/// § Conventions: Persistence / DB access).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ProjectorUser, IdentityRole<Guid>, Guid>(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
