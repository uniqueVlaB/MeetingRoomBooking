using MeetingRooms.Api.Config;
using MeetingRooms.Core.Abstractions;
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

        await SeedRolesAsync(provider, logger, cancellationToken);
        await SeedAccountAsync(provider, seedOptions.Admin, RoleNames.Admin, logger);
        await SeedAccountAsync(provider, seedOptions.User, RoleNames.User, logger);

        if (seedOptions.SampleRooms)
        {
            await SeedSampleRoomsAsync(
                dbContext,
                provider.GetRequiredService<IDatabaseConflictDetector>(),
                logger,
                cancellationToken);
        }
    }

    /// <summary>
    /// Creates any role that does not yet exist.
    /// </summary>
    /// <remarks>
    /// A role that fails to be created is fatal. Everything downstream — the seeded administrator,
    /// every <c>[Authorize(Roles = ...)]</c> endpoint — silently degrades to "no access", which is a
    /// far more expensive thing to diagnose than a refusal to start.
    /// </remarks>
    /// <param name="provider">Scoped service provider.</param>
    /// <param name="logger">Logger for reporting failures.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private static async Task SeedRolesAsync(
        IServiceProvider provider,
        ILogger logger,
        CancellationToken cancellationToken)
    {
        var roleManager = provider.GetRequiredService<RoleManager<ApplicationRole>>();

        foreach (var roleName in RoleNames.All)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var created = await roleManager.CreateAsync(new ApplicationRole(roleName));

            if (created.Succeeded)
            {
                continue;
            }

            // Another instance starting at the same moment may have created it a moment ago, which
            // is not a failure. Anything else is.
            if (await roleManager.RoleExistsAsync(roleName))
            {
                continue;
            }

            var errors = string.Join("; ", created.Errors.Select(error => error.Description));
            logger.LogCritical("Could not create the {Role} role: {Errors}", roleName, errors);

            throw new InvalidOperationException($"Could not create the '{roleName}' role: {errors}");
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

        var granted = await userManager.AddToRoleAsync(user, roleName);

        if (!granted.Succeeded)
        {
            // A seed account without its role is worse than no seed account: it signs in and then
            // refuses everything, which looks like a broken deployment rather than a seeding
            // problem. Remove it so the log is the only evidence, and say so loudly.
            logger.LogError(
                "Could not grant {Role} to the seed account; the account was removed. {Errors}",
                roleName,
                string.Join("; ", granted.Errors.Select(error => error.Description)));

            await userManager.DeleteAsync(user);
            return;
        }

        logger.LogInformation("Created the seed account for role {Role}.", roleName);
    }

    /// <summary>Creates two example rooms when the catalogue is empty.</summary>
    /// <param name="dbContext">The database context.</param>
    /// <param name="conflictDetector">Recognises a duplicate room name from a competing seeder.</param>
    /// <param name="logger">Logger for reporting what was created.</param>
    /// <param name="cancellationToken">Cancels the operation.</param>
    private static async Task SeedSampleRoomsAsync(
        AppDbContext dbContext,
        IDatabaseConflictDetector conflictDetector,
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

        try
        {
            await dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (conflictDetector.IsUniqueViolation(exception))
        {
            // "No rooms yet, so add some" is a read followed by a write, and two instances starting
            // together can both pass the read. The unique index on Name settles it; losing that race
            // means the rooms exist, which is all this wanted. Crashing here would take a healthy
            // instance down on boot for no reason.
            logger.LogInformation("Example rooms were seeded by another instance.");
            return;
        }

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
