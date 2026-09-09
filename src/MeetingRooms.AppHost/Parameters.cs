namespace MeetingRooms.AppHost;

/// <summary>
/// Groups the AppHost's parameters so <c>AppHost.cs</c> reads as a description of the system rather
/// than a wall of <c>AddParameter</c> calls.
/// </summary>
/// <remarks>
/// Real values live in the AppHost's user secrets under the <c>Parameters:</c> prefix and are never
/// committed. Development defaults are supplied for everything so that a fresh clone runs without
/// any setup; they are obviously-fake values that would be useless if they ever reached a deployed
/// environment, and Azure supplies its own through Web App application settings regardless.
/// </remarks>
internal static class Parameters
{
    /// <summary>
    /// A local-only signing key. Long enough for HMAC-SHA256 and clearly labelled, so it cannot be
    /// mistaken for something safe to deploy.
    /// </summary>
    private const string DevelopmentSigningKey =
        "local-development-signing-key-not-for-any-deployed-environment";

    /// <summary>Collects the JWT parameters.</summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <returns>The JWT parameters.</returns>
    internal static JwtParameters AddJwtParameters(this IDistributedApplicationBuilder builder) => new(
        SigningKey: builder.AddParameterWithDefault("JwtSigningKey", DevelopmentSigningKey, secret: true),
        Issuer: builder.AddParameterWithDefault("JwtIssuer", "MeetingRoomsApi"),
        Audience: builder.AddParameterWithDefault("JwtAudience", "MeetingRoomsClient"));

    /// <summary>Collects the seed-account parameters.</summary>
    /// <param name="builder">The distributed application builder.</param>
    /// <returns>The seed parameters.</returns>
    internal static SeedParameters AddSeedParameters(this IDistributedApplicationBuilder builder) => new(
        AdminPassword: builder.AddParameterWithDefault("SeedAdminPassword", "Admin!23456", secret: true),
        UserPassword: builder.AddParameterWithDefault("SeedUserPassword", "User!23456", secret: true));

    /// <summary>
    /// Declares a parameter, falling back to a development default when user secrets do not set it.
    /// </summary>
    /// <remarks>
    /// Without the fallback, a first run stops to ask for every value, which makes "clone and run"
    /// untrue. With it, secrets set in user secrets still win, because configuration is read first.
    /// </remarks>
    /// <param name="builder">The distributed application builder.</param>
    /// <param name="name">Parameter name, as used under <c>Parameters:</c> in user secrets.</param>
    /// <param name="developmentDefault">Value to use when nothing is configured.</param>
    /// <param name="secret">Whether the value should be masked in the dashboard.</param>
    /// <returns>The parameter resource.</returns>
    private static IResourceBuilder<ParameterResource> AddParameterWithDefault(
        this IDistributedApplicationBuilder builder,
        string name,
        string developmentDefault,
        bool secret = false)
    {
        var configured = builder.Configuration[$"Parameters:{name}"];

        return builder.AddParameter(
            name,
            string.IsNullOrWhiteSpace(configured) ? developmentDefault : configured,
            secret: secret);
    }
}

/// <summary>Parameters governing access-token issuance.</summary>
/// <param name="SigningKey">Symmetric HMAC-SHA256 signing key.</param>
/// <param name="Issuer">Token issuer.</param>
/// <param name="Audience">Token audience.</param>
internal sealed record JwtParameters(
    IResourceBuilder<ParameterResource> SigningKey,
    IResourceBuilder<ParameterResource> Issuer,
    IResourceBuilder<ParameterResource> Audience);

/// <summary>Passwords for the accounts created on first run.</summary>
/// <param name="AdminPassword">Password for the seeded administrator.</param>
/// <param name="UserPassword">Password for the seeded ordinary user.</param>
internal sealed record SeedParameters(
    IResourceBuilder<ParameterResource> AdminPassword,
    IResourceBuilder<ParameterResource> UserPassword);
