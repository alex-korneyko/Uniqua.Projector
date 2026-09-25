using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uniqua.Projector.Domain.Boards;

namespace Uniqua.Projector.Infrastructure.Boards.Configurations;

/// <summary>
/// Maps a card (data-model.md § Cards). A card is read and written one at a time, so it is not a
/// navigation of the board; both of its relationships are declared from this side.
/// </summary>
internal sealed class CardConfiguration : IEntityTypeConfiguration<Card>
{
    public void Configure(EntityTypeBuilder<Card> builder)
    {
        builder.ToTable("Cards");
        builder.HasKey(card => card.Id);
        builder.Property(card => card.Id).ValueGeneratedNever();

        builder.Property(card => card.BoardId).IsRequired();
        builder.Property(card => card.ColumnId).IsRequired();
        builder.Property(card => card.Position).IsRequired();

        builder.Property(card => card.Title)
            .IsRequired()
            .HasMaxLength(BoardText.StorageLength(Card.MaxTitleLength));

        // No max length: the Text rule bounds a description, not the storage width.
        builder.Property(card => card.Description).IsRequired();

        // ADR 0016: an edit or delete is refused when the title or description moved on.
        builder.Property(card => card.ContentVersion).IsRequired().IsConcurrencyToken();

        builder.HasOne<Board>()
            .WithMany()
            .HasForeignKey(card => card.BoardId)
            .OnDelete(DeleteBehavior.Cascade);

        // NO ACTION: SQL Server refuses a second cascade path into Cards, and the store then backs
        // up AC-09 — a column row that still has cards cannot be deleted.
        builder.HasOne<Column>()
            .WithMany()
            .HasForeignKey(card => card.ColumnId)
            .OnDelete(DeleteBehavior.NoAction);

        // Card summaries when a board is opened (ADR 0018), covered; also serves the BoardId FK.
        builder.HasIndex(card => new { card.BoardId, card.ColumnId, card.Position })
            .HasDatabaseName("IX_Cards_BoardId_ColumnId_Position")
            .IncludeProperties(card => new { card.Title, card.ContentVersion });

        // The NO ACTION check on every column deletion.
        builder.HasIndex(card => card.ColumnId)
            .HasDatabaseName("IX_Cards_ColumnId");
    }
}
