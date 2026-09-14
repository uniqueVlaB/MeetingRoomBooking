using System.Diagnostics.CodeAnalysis;
using MeetingRooms.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingRooms.Infrastructure.SQL.Database.Configuration;

/// <summary>Maps <see cref="RefreshToken"/>.</summary>
[ExcludeFromCodeCoverage]
public sealed class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // A hex-encoded SHA-256 hash is always 64 characters; fixing the length keeps the unique
        // index narrow.
        builder.Property(t => t.TokenHash).HasMaxLength(64).IsRequired();
        builder.HasIndex(t => t.TokenHash).IsUnique();

        // Rotation reads a token and then revokes it; the rowversion is what stops two concurrent
        // refreshes from both redeeming the same one. See the remarks on RefreshToken.RowVersion.
        builder.Property(t => t.RowVersion).IsRowVersion();

        builder.HasOne(t => t.User)
            .WithMany(u => u.RefreshTokens)
            .HasForeignKey(t => t.UserId)
            // Deleting a user should take their tokens with them; unlike bookings, they are not
            // history worth keeping.
            .OnDelete(DeleteBehavior.Cascade);
    }
}
