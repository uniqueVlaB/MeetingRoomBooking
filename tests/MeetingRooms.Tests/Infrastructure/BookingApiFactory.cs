using MeetingRooms.Api.Auth;
using MeetingRooms.Core.Entities;
using MeetingRooms.Infrastructure.SQL;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace MeetingRooms.Tests.Infrastructure;

/// <summary>
/// Boots the real API pipeline against the test database.
/// </summary>
/// <remarks>
/// The whole pipeline runs — routing, authentication, model binding, the booking service — because
/// the concurrency requirement is about what a <em>client</em> observes: exactly one 201 and a clean
/// 409 for everyone else. Testing the service in isolation would not prove the status codes, which
/// are the part of the contract the task actually specifies. Azure SignalR is left unconfigured, so
/// the in-process hub is used and no subscription is needed.
/// </remarks>
/// <param name="connectionString">Connection string for the test database.</param>
public sealed class BookingApiFactory(string connectionString) : WebApplicationFactory<Program>
{
    /// <summary>Signing key used for tokens in tests. Long enough for HMAC-SHA256.</summary>
    private const string TestSigningKey = "test-signing-key-for-integration-tests-only-32-plus-chars";

    private readonly string connectionString = connectionString;
    private readonly List<string> serverErrors = [];

    /// <summary>
    /// Errors the API logged during the test.
    /// </summary>
    /// <remarks>
    /// The pipeline turns unhandled exceptions into problem details, which is right for clients but
    /// leaves a failing test looking at a bare 500. Capturing the log means the assertion message can
    /// say what actually went wrong.
    /// </remarks>
    public IReadOnlyList<string> ServerErrors
    {
        get
        {
            lock (this.serverErrors)
            {
                return [.. this.serverErrors];
            }
        }
    }

    /// <summary>Renders captured errors for an assertion message.</summary>
    /// <returns>The logged errors, or a note that there were none.</returns>
    public string DescribeServerErrors()
    {
        var errors = this.ServerErrors;

        return errors.Count == 0
            ? "(the API logged no errors)"
            : string.Join(Environment.NewLine, errors);
    }

    /// <summary>
    /// Issues a bearer token for a user, through the same token service the API uses at runtime.
    /// </summary>
    /// <remarks>
    /// Using the real service rather than hand-rolling a token means the test cannot accidentally
    /// pass with a token the deployed API would reject.
    /// </remarks>
    /// <param name="user">The user to authenticate as.</param>
    /// <param name="roles">Roles to embed in the token.</param>
    /// <returns>The signed JWT.</returns>
    public string CreateAccessToken(ApplicationUser user, params string[] roles)
    {
        using var scope = this.Services.CreateScope();
        var tokenService = scope.ServiceProvider.GetRequiredService<ITokenService>();

        return tokenService.CreateAccessToken(user, roles).Value;
    }

    /// <inheritdoc />
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        // Not "Development": that would map the Scalar UI these tests do not need, and pick up the
        // development seed settings. The fixture has already migrated.
        builder.UseEnvironment("Testing");

        // UseSetting rather than ConfigureAppConfiguration. Program.cs reads configuration while the
        // builder is still being composed -- AddSqlServerDbContext resolves its connection string
        // there -- and ConfigureAppConfiguration delegates are applied after that point, so the
        // values would arrive too late to be seen.
        var settings = new Dictionary<string, string?>
        {
            [$"ConnectionStrings:{DependencyInjection.DatabaseResourceName}"] = this.connectionString,
            ["Jwt:Issuer"] = "https://meetingrooms.tests",
            ["Jwt:Audience"] = "https://meetingrooms.tests",
            ["Jwt:SigningKey"] = TestSigningKey,
            ["Jwt:AccessTokenMinutes"] = "60",

            // Empty, so the in-process hub is used and no Azure subscription is needed.
            ["Azure:SignalR:ConnectionString"] = string.Empty,

            // The fixture already migrated, and the tests create their own data.
            ["Database:MigrateOnStartup"] = "false",
            ["Seed:Enabled"] = "false",
            ["Cors:AllowedOrigins:0"] = "http://localhost:4200",
        };

        foreach (var (key, value) in settings)
        {
            builder.UseSetting(key, value);
        }

        builder.ConfigureLogging(logging => logging.AddProvider(new CapturingLoggerProvider(entry =>
        {
            lock (this.serverErrors)
            {
                this.serverErrors.Add(entry);
            }
        })));
    }

    /// <summary>Forwards logged errors to a callback so tests can report them.</summary>
    /// <param name="onError">Receives each formatted error entry.</param>
    private sealed class CapturingLoggerProvider(Action<string> onError) : ILoggerProvider
    {
        /// <inheritdoc />
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(categoryName, onError);

        /// <inheritdoc />
        public void Dispose()
        {
        }

        /// <summary>Records entries logged at error level or above.</summary>
        /// <param name="category">The logger category.</param>
        /// <param name="onError">Receives each formatted error entry.</param>
        private sealed class CapturingLogger(string category, Action<string> onError) : ILogger
        {
            /// <inheritdoc />
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            /// <inheritdoc />
            public bool IsEnabled(LogLevel logLevel) => logLevel >= LogLevel.Error;

            /// <inheritdoc />
            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (!this.IsEnabled(logLevel))
                {
                    return;
                }

                ArgumentNullException.ThrowIfNull(formatter);

                onError($"[{category}] {formatter(state, exception)}{Environment.NewLine}{exception}");
            }
        }
    }
}
