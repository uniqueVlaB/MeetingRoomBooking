using MeetingRooms.Api.Auth;
using MeetingRooms.Api.Realtime;
using MeetingRooms.Core.Options;

namespace MeetingRooms.Api.Config;

/// <summary>Registers the HTTP and real-time surface.</summary>
public static class ApiConfiguration
{
    /// <summary>Name of the CORS policy applied to the whole API.</summary>
    public const string CorsPolicyName = "SpaClient";

    /// <summary>
    /// Adds controllers, CORS, problem details, OpenAPI and the SignalR transport.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static WebApplicationBuilder AddApiServices(this WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddOptionsConfiguration();
        builder.AddAuthSupportServices();
        builder.Services.AddCorsConfiguration(builder.Configuration);
        builder.AddRealtimeConfiguration();

        builder.Services.AddControllers();

        // RFC 9457 problem details for every error response, including ones produced by the
        // framework, so clients see one error shape.
        builder.Services.AddProblemDetails();
        builder.Services.AddOpenApi();

        return builder;
    }

    /// <summary>Binds and validates strongly typed options.</summary>
    /// <param name="builder">The host application builder.</param>
    private static void AddOptionsConfiguration(this WebApplicationBuilder builder)
    {
        // ValidateOnStart means a missing signing key stops the process at boot rather than failing
        // the first sign-in, which in Azure would otherwise look like a working deployment.
        builder.Services.AddOptions<JwtOptions>()
            .Bind(builder.Configuration.GetSection(JwtOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.AddOptions<RefreshTokenOptions>()
            .Bind(builder.Configuration.GetSection(RefreshTokenOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();

        builder.Services.Configure<RefreshTokenCookieOptions>(
            builder.Configuration.GetSection(RefreshTokenCookieOptions.SectionName));

        builder.Services.Configure<SeedOptions>(
            builder.Configuration.GetSection(SeedOptions.SectionName));

        // The time zone is validated when ScheduleClock is first resolved rather than here, because
        // "is this a zone this machine knows?" is not something data annotations can express.
        builder.Services.AddOptions<BookingOptions>()
            .Bind(builder.Configuration.GetSection(BookingOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
    }

    /// <summary>Registers the pieces of the auth surface that live in the API layer.</summary>
    /// <param name="builder">The host application builder.</param>
    private static void AddAuthSupportServices(this WebApplicationBuilder builder)
    {
        builder.Services.AddScoped<ITokenService, JwtTokenService>();
        builder.Services.AddSingleton<RefreshTokenCookie>();
    }

    /// <summary>
    /// Restricts browser access to the configured client origins.
    /// </summary>
    /// <remarks>
    /// Origins come from configuration rather than a literal, because the client is a separate Azure
    /// Web App whose URL differs per environment. <c>AllowCredentials</c> is required for both the
    /// refresh cookie and the SignalR negotiate request, and it is incompatible with a wildcard
    /// origin — so an empty configured list means no browser origin is allowed, which fails visibly
    /// rather than silently allowing everything.
    /// </remarks>
    /// <param name="services">The service collection.</param>
    /// <param name="configuration">Application configuration.</param>
    private static void AddCorsConfiguration(this IServiceCollection services, IConfiguration configuration)
    {
        var allowedOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

        services.AddCors(options => options.AddPolicy(
            CorsPolicyName,
            policy => policy
                .WithOrigins(allowedOrigins)
                .AllowAnyHeader()
                .AllowAnyMethod()
                .AllowCredentials()));
    }

    /// <summary>
    /// Adds SignalR, using Azure SignalR Service when a connection string is configured.
    /// </summary>
    /// <remarks>
    /// The fallback to the in-process hub is deliberate: it lets the whole application run locally,
    /// and the tests run in CI, without an Azure subscription. In Azure the connection string is
    /// present and fan-out moves to the managed service, which is what allows the Web App to scale
    /// beyond one instance.
    /// </remarks>
    /// <param name="builder">The host application builder.</param>
    private static void AddRealtimeConfiguration(this WebApplicationBuilder builder)
    {
        var signalR = builder.Services.AddSignalR();
        var azureSignalRConnectionString = builder.Configuration["Azure:SignalR:ConnectionString"];

        if (!string.IsNullOrWhiteSpace(azureSignalRConnectionString))
        {
            signalR.AddAzureSignalR(azureSignalRConnectionString);
        }

        builder.Services.AddScoped<IBookingNotifier, SignalRBookingNotifier>();
    }
}
