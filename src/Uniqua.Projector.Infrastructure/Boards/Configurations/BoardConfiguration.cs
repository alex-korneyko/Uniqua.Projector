using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Infrastructure.Boards.Configurations;

/// <summary>
/// Maps the board aggregate root (data-model.md § Boards). The columns and memberships it holds are
/// reached through its own backing fields, so the aggregate stays the only way to change them.
/// </summary>
internal sealed class BoardConfiguration : IEntityTypeConfiguration<Board>
{
    /// <summary>
    /// ADR 0015's write condition, on both the board and the owned-board counter. A shadow property:
    /// it is a fact of the store rather than of the domain, and it is never sent to the client.
    /// </summary>
    internal const string RowVersion = "RowVersion";

    public void Configure(EntityTypeBuilder<Board> builder)
    {
        builder.ToTable("Boards");
        builder.HasKey(board => board.Id);

        // Allocated by the application as a GUID v7 (CLAUDE.md § Ids).
        builder.Property(board => board.Id).ValueGeneratedNever();

        builder.Property(board => board.Name)
            .IsRequired()
            .HasMaxLength(BoardText.StorageLength(Board.MaxNameLength));

        builder.Property(board => board.CreatedAt).IsRequired();
        builder.Property(board => board.CardCount).IsRequired();
        builder.Property(board => board.ColumnLayoutVersion).IsRequired();

        builder.Property<byte[]>(RowVersion).IsRequired().IsRowVersion();

        builder.HasMany(board => board.Columns)
            .WithOne()
            .HasForeignKey(column => column.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasMany(board => board.Memberships)
            .WithOne()
            .HasForeignKey(membership => membership.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Navigation(board => board.Columns).UsePropertyAccessMode(PropertyAccessMode.Field);
        builder.Navigation(board => board.Memberships).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
