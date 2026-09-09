using System.Diagnostics.CodeAnalysis;
using MeetingRooms.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingRooms.Infrastructure.SQL.Database.Configuration;

/// <summary>Maps <see cref="TimeSlot"/>.</summary>
[ExcludeFromCodeCoverage]
public sealed class TimeSlotConfiguration : IEntityTypeConfiguration<TimeSlot>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TimeSlot> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.HasOne(s => s.Room)
            .WithMany(r => r.TimeSlots)
            .HasForeignKey(s => s.RoomId)
            .OnDelete(DeleteBehavior.Cascade);

        // A room's template must not contain the same start time twice, otherwise the schedule view
        // would show two indistinguishable rows and a user could not tell which one they booked.
        builder.HasIndex(s => new { s.RoomId, s.StartTime }).IsUnique();
    }
}
