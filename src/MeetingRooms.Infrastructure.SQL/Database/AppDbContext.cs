using MeetingRooms.Core.Entities;
using MeetingRooms.Infrastructure.SQL.Database.Configuration;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MeetingRooms.Infrastructure.SQL.Database;

/// <summary>
/// EF Core context for the booking system, including the ASP.NET Core Identity tables.
/// </summary>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : IdentityDbContext<ApplicationUser, ApplicationRole, Guid>(options)
{
    /// <summary>Name of the unique index that makes double-booking impossible.</summary>
    /// <remarks>
    /// Referenced by <c>SqlServerSlotConflictDetector</c> when deciding whether a failed save was a
    /// lost booking race, and asserted on by the concurrency tests. Naming the index explicitly —
    /// rather than accepting EF's generated name — is what lets the detector distinguish this
    /// conflict from any other unique violation.
    /// </remarks>
    public const string ActiveSlotIndexName = "UX_Bookings_ActiveSlot";

    /// <summary>Bookable rooms.</summary>
    public DbSet<Room> Rooms => this.Set<Room>();

    /// <summary>The fixed daily slot templates owned by rooms.</summary>
    public DbSet<TimeSlot> TimeSlots => this.Set<TimeSlot>();

    /// <summary>Bookings of a slot on a date.</summary>
    public DbSet<Booking> Bookings => this.Set<Booking>();

    /// <summary>Refresh tokens issued to users.</summary>
    public DbSet<RefreshToken> RefreshTokens => this.Set<RefreshToken>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        EntityConfigurator.ConfigureEntities(builder);
    }
}
