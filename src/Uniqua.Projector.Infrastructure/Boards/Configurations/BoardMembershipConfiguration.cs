using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uniqua.Projector.Domain.Boards;
using Uniqua.Projector.Infrastructure.Accounts;

namespace Uniqua.Projector.Infrastructure.Boards.Configurations;

/// <summary>
/// Maps a board membership (data-model.md § BoardMemberships, ADR 0013). One record per
/// (board, account) and one owner per board are stated here as database facts, backing what the
/// aggregate already enforces rather than replacing it.
/// </summary>
internal sealed class BoardMembershipConfiguration : IEntityTypeConfiguration<BoardMembership>
{
    /// <summary>Wide enough for the longest <see cref="BoardRole"/> name as stored.</summary>
    private const int RoleMaxLength = 10;

    public void Configure(EntityTypeBuilder<BoardMembership> builder)
    {
        builder.ToTable("BoardMemberships");
        builder.HasKey(membership => membership.Id);
        builder.Property(membership => membership.Id).ValueGeneratedNever();

        builder.Property(membership => membership.BoardId).IsRequired();
        builder.Property(membership => membership.AccountId).IsRequired();

        builder.Property(membership => membership.Role)
            .IsRequired()
            .HasConversion<string>()
            .HasMaxLength(RoleMaxLength);

        // NO ACTION: cascading an account deletion would leave an ownerless, invisible board.
        builder.HasOne<ProjectorUser>()
            .WithMany()
            .HasForeignKey(membership => membership.AccountId)
            .OnDelete(DeleteBehavior.NoAction);

        // The member-scoped load (ADR 0014); one record per (board, account) (ADR 0013).
        builder.HasIndex(membership => new { membership.BoardId, membership.AccountId })
            .HasDatabaseName("IX_BoardMemberships_BoardId_AccountId")
            .IsUnique();

        // "My boards" with the owned flag, covered; also serves the AccountId FK.
        builder.HasIndex(membership => new { membership.AccountId, membership.BoardId })
            .HasDatabaseName("IX_BoardMemberships_AccountId_BoardId")
            .IncludeProperties(membership => membership.Role);

        // Exactly one Owner per board (ADR 0013). The filter must match the converted value.
        builder.HasIndex(membership => membership.BoardId, "IX_BoardMemberships_BoardId_Owner")
            .IsUnique()
            .HasFilter($"[Role] = N'{nameof(BoardRole.Owner)}'");
    }
}
