using MeetingRooms.Api.Config;
using MeetingRooms.Core.Entities;
using MeetingRooms.Infrastructure.SQL.Database;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace MeetingRooms.Api.Infrastructure;

/// <summary>
/// Brings the database schema up to date and seeds the accounts and rooms needed to use the app.
/// </summary>
public static class DatabaseInitializer
{
    /// <summary>Configuration key controlling whether migrations run at start-up.</summary>
    public const string MigrateOnStartupKey = "Database:MigrateOnStartup";

    /// <summary>
    /// Applies migrations if enabled, then seeds roles, accounts and example rooms.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Migrating on start-up is a deliberate trade-off. The deployment pipeline authenticates with a
    /// publish profile, which gives it no way to open the Azure SQL firewall for a GitHub runner, so
    /// running <c>efbundle</c> from CI is not available without adding federated credentials.
    /// EF Core takes an exclusive migration lock, so two instances starting together cannot apply
    /// the same migration twice — one waits for the other. <c>docs/deployment.md</c> records the
    /// alternative and why it was not taken.
    /// </para>
    /// <para>
    /// Everything here is idempotent, because it runs on every start, not only the first.
    /// </para>
    /// </remarks>
    /// <param name="services">A scope provider for the application's services.</param>
    /// <param name="configuration">Application configuration.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    /// <returns>A task that completes when the database is ready.</returns>
    public static async Task InitializeAsync(
        IServiceProvider services,
        IConfiguration configuration,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        await using var scope = services.CreateAsyncScope();
        var provider = scope.ServiceProvider;

        var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));
        var dbContext = provider.GetRequiredService<AppDbContext>();

        if (configuration.GetValue(MigrateOnStartupKey, defaultValue: false))
        {
            logger.LogInformation("Applying database migrations.");
            await dbContext.Database.MigrateAsync(cancellationToken);
        }

        var seedOptions = provider.GetRequiredService<IOptions<SeedOptions>>().Value;

        if (!seedOptions.Enabled)
        {
            return;
        }

        await SeedRolesAsync(provider, cancellationToken);
        await SeedAccountAsync(provider, seedOptions.Admin, RoleNames.Admin, logger);
        await SeedAccountAsync(provider, seedOptions.User, RoleNames.User, logger);

        if (seedOptions.SampleRooms)
        {
            await SeedSampleRoomsAsync(dbContext, logger, cancellationToken);
        }
    }

    /// <summary>Creates any role that does not yet exist.</summary>
    /// <param name="provider">Scoped service provider.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private static async Task SeedRolesAsync(IServiceProvider provider, CancellationToken cancellationToken)
    {
        var roleManager = provider.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var roleName in RoleNames.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (!await roleManager.RoleExistsAsync(roleName))
            {
                await roleManager.CreateAsync(new ApplicationRole(roleName));
            }
        }
    }

    /// <summary>Creates a configured account and puts it in a role, if it does not already exist.</summary>
    /// <param name="provider">Scoped service provider.</param>
    /// <param name="account">The account to create.</param>
    /// <param name="roleName">The role to grant.</param>
    /// <param name="logger">Logger for reporting failures.</param>
    private static async Task SeedAccountAsync(
        IServiceProvider provider,
        SeedAccount account,
        string roleName,
        ILogger logger)
    {
        if (!account.IsConfigured())
        {
            logger.LogInformation(
                "No seed account configured for role {Role}; skipping.",
                roleName);
            return;
        }

        var userManager = provider.GetRequiredService<UserManager<ApplicationUser>>();

        if (await userManager.FindByEmailAsync(account.Email) is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = account.Email,
            Email = account.Email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(account.DisplayName)
                ? account.Email
                : account.DisplayName,
        };

        var created = await userManager.CreateAsync(user, account.Password);

        if (!created.Succeeded)
        {
            // Do not throw: a seed password that fails the strength rules should not stop the API
            // from starting and serving everyone else.
            logger.LogWarning(
                "Could not create the seed account for role {Role}: {Errors}",
                roleName,
                string.Join("; ", created.Errors.Select(error => error.Description)));
            return;
        }

        await userManager.AddToRoleAsync(user, roleName);
        logger.LogInformation("Created the seed account for role {Role}.", roleName);
    }

    /// <summary>Creates two example rooms when the catalogue is empty.</summary>
    /// <param name="dbContext">The database context.</param>
    /// <param name="logger">Logger for reporting what was created.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private static async Task SeedSampleRoomsAsync(
        AppDbContext dbContext,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        if (await dbContext.Rooms.AnyAsync(cancellationToken))
        {
            return;
        }

        dbContext.Rooms.AddRange(
            CreateRoom("Kyiv — Focus Room", "3rd floor", capacity: 4, startHour: 9, slots: 8),
            CreateRoom("Lviv — Board Room", "5th floor", capacity: 12, startHour: 9, slots: 8));

        await dbContext.SaveChangesAsync(cancellationToken);
        logger.LogInformation("Seeded example rooms.");
    }

    /// <summary>Builds a room with an hourly slot template.</summary>
    /// <param name="name">Room name.</param>
    /// <param name="location">Room location.</param>
    /// <param name="capacity">Seats.</param>
    /// <param name="startHour">Hour the first slot starts.</param>
    /// <param name="slots">How many one-hour slots to create.</param>
    /// <returns>The unsaved room.</returns>
    private static Room CreateRoom(string name, string location, int capacity, int startHour, int slots)
    {
        var room = new Room { Name = name, Location = location, Capacity = capacity };

        for (var index = 0; index < slots; index++)
        {
            room.TimeSlots.Add(new TimeSlot
            {
                RoomId = room.Id,
                StartTime = new TimeOnly(startHour + index, 0),
                EndTime = new TimeOnly(startHour + index + 1, 0),
                Ordinal = index,
            });
        }

        return room;
    }
}
