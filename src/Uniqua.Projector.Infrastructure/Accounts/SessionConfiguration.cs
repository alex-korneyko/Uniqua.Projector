using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Uniqua.Projector.Domain.Accounts;

namespace Uniqua.Projector.Infrastructure.Accounts;

/// <summary>
/// Maps the session record ADR 0008 chose over a self-contained cookie ticket. The table holds the
/// three timestamps the session rules are evaluated against; the rules themselves stay on the
/// entity, which is why no CHECK constrains their ordering — restating a rule here would put one
/// rule in two places that can disagree.
/// </summary>
internal sealed class SessionConfiguration : IEntityTypeConfiguration<Session>
{
    public void Configure(EntityTypeBuilder<Session> builder)
    {
        builder.ToTable("Sessions");
        builder.HasKey(session => session.Id);

        // Never database-generated: the id is the opaque reference the cookie carries, and it is
        // allocated by the application as a GUID v7.
        builder.Property(session => session.Id).ValueGeneratedNever();

        builder.Property(session => session.AccountId).IsRequired();
        builder.Property(session => session.CreatedAt).IsRequired();
        builder.Property(session => session.LastSeenAt).IsRequired();

        // NULL means live. A revoked row is kept until the sweep removes it, so AC-10 refuses on
        // the strength of a record rather than of an absence.
        builder.Property(session => session.RevokedAt).IsRequired(false);

        // Declared from this side only: the account deliberately has no navigation back to its
        // sessions, so reading a session can never drag the account graph along with it.
        builder.HasOne<ProjectorUser>()
            .WithMany()
            .HasForeignKey(session => session.AccountId)
            .OnDelete(DeleteBehavior.Cascade);

        // FK hygiene: the cascade from AspNetUsers finds this account's sessions by this column.
        builder.HasIndex(session => session.AccountId)
            .HasDatabaseName("IX_Sessions_AccountId");

        // The cleanup sweep, half one: sessions opened more than 90 days ago.
        builder.HasIndex(session => session.CreatedAt)
            .HasDatabaseName("IX_Sessions_CreatedAt");

        // The cleanup sweep, half two: rows revoked more than 14 days ago. Filtered, because
        // almost every row is live and carries NULL here — so the index stays a fraction of the
        // table and costs nothing on the common write path of opening a session.
        builder.HasIndex(session => session.RevokedAt)
            .HasDatabaseName("IX_Sessions_RevokedAt")
            .HasFilter("[RevokedAt] IS NOT NULL");

        // Deliberately NOT indexed: LastSeenAt. It is written on the hot path and never filtered
        // on — the 14-day rule is evaluated on a row already fetched by primary key.
    }
}
