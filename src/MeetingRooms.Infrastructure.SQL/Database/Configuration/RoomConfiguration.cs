using System.Diagnostics.CodeAnalysis;
using MeetingRooms.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingRooms.Infrastructure.SQL.Database.Configuration;

/// <summary>Maps <see cref="Room"/>.</summary>
[ExcludeFromCodeCoverage]
public sealed class RoomConfiguration : IEntityTypeConfiguration<Room>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Room> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(r => r.Name).HasMaxLength(128).IsRequired();
        builder.Property(r => r.Location).HasMaxLength(256);
        builder.Property(r => r.RowVersion).IsRowVersion();

        // Room names are how users identify a room, so they must be unambiguous.
        builder.HasIndex(r => r.Name).IsUnique();
    }
}
