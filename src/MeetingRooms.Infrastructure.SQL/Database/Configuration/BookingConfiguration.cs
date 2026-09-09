using System.Diagnostics.CodeAnalysis;
using MeetingRooms.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingRooms.Infrastructure.SQL.Database.Configuration;

/// <summary>
/// Maps <see cref="Booking"/>, including the unique index that prevents double-booking.
/// </summary>
[ExcludeFromCodeCoverage]
public sealed class BookingConfiguration : IEntityTypeConfiguration<Booking>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<Booking> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(b => b.RowVersion).IsRowVersion();

        // Stored as the enum's underlying int; the index filter below depends on this mapping.
        // Storing it as a string would make the filter "[Status] = 0" match nothing, which would
        // disable the guarantee without any error.
        builder.Property(b => b.Status).HasConversion<int>();

        builder.HasOne(b => b.TimeSlot)
            .WithMany(s => s.Bookings)
            .HasForeignKey(b => b.TimeSlotId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(b => b.User)
            .WithMany(u => u.Bookings)
            .HasForeignKey(b => b.UserId)
            // Users are not deleted while they hold bookings; fail loudly rather than silently
            // freeing somebody else's meeting.
            .OnDelete(DeleteBehavior.Restrict);

        // -----------------------------------------------------------------------------------------
        // THE concurrency guarantee. See docs/concurrency.md before changing anything here.
        //
        // A partial unique index over (TimeSlotId, SlotDate) restricted to active rows means the
        // database itself rejects a second active booking for a slot. Creating a booking is
        // therefore a single INSERT with no preceding "is it free?" read, so there is no window
        // between check and write for a competing request to slip through. SQL Server serialises
        // the contenders on the index; the losers' inserts simply never happen.
        //
        // Cancelled rows are excluded by the filter, which is what lets a released slot be booked
        // again while preserving the cancelled row as history.
        //
        // The literal 0 is BookingStatus.Active — see the remarks on that enum before renumbering.
        // -----------------------------------------------------------------------------------------
        builder.HasIndex(b => new { b.TimeSlotId, b.SlotDate })
            .IsUnique()
            .HasFilter("[Status] = 0")
            .HasDatabaseName(AppDbContext.ActiveSlotIndexName);

        // Supports "my bookings" and the admin cross-user listing.
        builder.HasIndex(b => new { b.UserId, b.SlotDate });
    }
}
