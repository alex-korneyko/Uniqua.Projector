using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Infrastructure.Boards.Configurations;

/// <summary>
/// Maps a column of a board (data-model.md § Columns). Its relationship to the board is declared
/// from the board's side, in <see cref="BoardConfiguration"/>.
/// </summary>
internal sealed class ColumnConfiguration : IEntityTypeConfiguration<Column>
{
    public void Configure(EntityTypeBuilder<Column> builder)
    {
        builder.ToTable("Columns");
        builder.HasKey(column => column.Id);
        builder.Property(column => column.Id).ValueGeneratedNever();

        builder.Property(column => column.BoardId).IsRequired();

        builder.Property(column => column.Name)
            .IsRequired()
            .HasMaxLength(BoardText.StorageLength(Column.MaxNameLength));

        builder.Property(column => column.Position).IsRequired();
        builder.Property(column => column.CardCount).IsRequired();
        builder.Property(column => column.NextCardPosition).IsRequired();

        // ADR 0016: a rename or delete is refused when the name moved on since it was seen.
        builder.Property(column => column.NameVersion).IsRequired().IsConcurrencyToken();

        // The board's columns in position order; also serves the BoardId FK. Deliberately NOT
        // unique: a renumber is one UPDATE per row, checked per statement (ADR 0017).
        builder.HasIndex(column => new { column.BoardId, column.Position })
            .HasDatabaseName("IX_Columns_BoardId_Position");
    }
}
