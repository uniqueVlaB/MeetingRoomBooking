namespace MeetingRooms.Api.Config;

/// <summary>An account created at start-up if it does not already exist.</summary>
public sealed class SeedAccount
{
    /// <summary>Email address, which doubles as the user name.</summary>
    public string Email { get; set; } = string.Empty;

    /// <summary>Name shown beside the account's bookings.</summary>
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>
    /// Initial password.
    /// </summary>
    /// <remarks>
    /// Supplied through configuration — user secrets locally, Web App settings in Azure — and never
    /// committed. Hardcoding a seed credential in source puts the production administrator password
    /// in the repository, where it outlives every rotation.
    /// </remarks>
    public string Password { get; set; } = string.Empty;

    /// <summary>Whether this account has anything to create.</summary>
    /// <returns><see langword="true"/> when an email and a password were configured.</returns>
    public bool IsConfigured() =>
        !string.IsNullOrWhiteSpace(this.Email) && !string.IsNullOrWhiteSpace(this.Password);
}

/// <summary>Controls what the application creates on start-up when the database is empty.</summary>
public sealed class SeedOptions
{
    /// <summary>Configuration section these options bind to.</summary>
    public const string SectionName = "Seed";

    /// <summary>Whether to seed at all. Off unless explicitly enabled.</summary>
    public bool Enabled { get; set; }

    /// <summary>The administrator account to create, if configured.</summary>
    public SeedAccount Admin { get; set; } = new();

    /// <summary>An ordinary user account to create, if configured.</summary>
    public SeedAccount User { get; set; } = new();

    /// <summary>
    /// Whether to create a couple of example rooms when no rooms exist.
    /// </summary>
    /// <remarks>
    /// Only when the table is empty, so this never re-adds rooms an administrator has deleted.
    /// </remarks>
    public bool SampleRooms { get; set; } = true;
}
