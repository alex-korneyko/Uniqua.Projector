using Microsoft.EntityFrameworkCore;

namespace Uniqua.Projector.Infrastructure;

/// <summary>
/// The single EF Core context. It lives in Infrastructure and is reachable from nowhere else —
/// Domain does not know it exists and Api only registers it (see docs/architecture-map.md
/// § Conventions: Persistence / DB access).
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}
