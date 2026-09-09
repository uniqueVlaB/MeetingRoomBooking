using System.Diagnostics.CodeAnalysis;
using MeetingRooms.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace MeetingRooms.Infrastructure.SQL.Database.Configuration;

/// <summary>Maps the parts of <see cref="ApplicationUser"/> that Identity does not already map.</summary>
[ExcludeFromCodeCoverage]
public sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Property(u => u.DisplayName).HasMaxLength(128).IsRequired();
    }
}
