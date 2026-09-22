using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Uniqua.Projector.Infrastructure.Accounts;

/// <summary>
/// Turns two of this feature's promises into facts of the database rather than hopes of the code:
/// AC-03 ("an email address identifies exactly one account") and AC-11b ("a display name
/// identifies exactly one account"). Identity's own model declares both indexes, but only one of
/// them unique — this configuration overrides that.
/// </summary>
internal sealed class ProjectorUserConfiguration : IEntityTypeConfiguration<ProjectorUser>
{
    /// <summary>AC-01's ceiling on a display name, applied to the stored column and its key.</summary>
    private const int DisplayNameMaxLength = 50;

    public void Configure(EntityTypeBuilder<ProjectorUser> builder)
    {
        builder.Property(user => user.DisplayName)
            .IsRequired()
            .HasMaxLength(DisplayNameMaxLength);

        builder.Property(user => user.NormalizedDisplayName)
            .IsRequired()
            .HasMaxLength(DisplayNameMaxLength);

        // AC-03. Identity's default EmailIndex is NON-unique; this feature makes it unique.
        // Filtered, because Identity leaves NormalizedEmail nullable and SQL Server would otherwise
        // count two unknown addresses as the same address.
        builder.HasIndex(user => user.NormalizedEmail)
            .HasDatabaseName("EmailIndex")
            .IsUnique()
            .HasFilter("[NormalizedEmail] IS NOT NULL");

        // AC-11b. No filter needed: NormalizedDisplayName is NOT NULL.
        builder.HasIndex(user => user.NormalizedDisplayName)
            .HasDatabaseName("DisplayNameIndex")
            .IsUnique();
    }
}
