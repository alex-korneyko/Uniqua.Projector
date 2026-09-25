using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uniqua.Projector.Domain.Boards;
using Uniqua.Projector.Infrastructure.Accounts;

namespace Uniqua.Projector.Infrastructure.Boards.Configurations;

/// <summary>
/// Maps the per-account owned-board counter (data-model.md § OwnedBoardCounters, ADR 0015): one
/// row per account, keyed by the account itself, whose row version makes two simultaneous board
/// creations by one account collide.
/// </summary>
internal sealed class OwnedBoardCounterConfiguration : IEntityTypeConfiguration<OwnedBoardCounter>
{
    public void Configure(EntityTypeBuilder<OwnedBoardCounter> builder)
    {
        builder.ToTable("OwnedBoardCounters");
        builder.HasKey(counter => counter.AccountId);
        builder.Property(counter => counter.AccountId).ValueGeneratedNever();

        builder.Property(counter => counter.OwnedBoardCount).IsRequired();

        builder.Property<byte[]>(BoardConfiguration.RowVersion).IsRequired().IsRowVersion();

        builder.HasOne<ProjectorUser>()
            .WithOne()
            .HasForeignKey<OwnedBoardCounter>(counter => counter.AccountId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
