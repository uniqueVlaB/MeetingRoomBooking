using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace MeetingRooms.ServiceDefaults;

/// <summary>
/// Cross-cutting wiring shared by every service in the solution: telemetry, health checks, service
/// discovery and HTTP resilience.
/// </summary>
/// <remarks>
/// Based on the .NET Aspire service-defaults template. The point of keeping it in a shared project
/// is that a new service gets consistent observability by calling one method, instead of copying
/// twenty lines that then drift.
/// </remarks>
public static class Extensions
{
    /// <summary>Health-check tag marking checks that must pass for the service to be considered live.</summary>
    private const string LiveTag = "live";

    /// <summary>
    /// Adds telemetry, default health checks, service discovery and resilient HTTP defaults.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder AddServiceDefaults<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.ConfigureOpenTelemetry();
        builder.AddDefaultHealthChecks();

        builder.Services.AddServiceDiscovery();

        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            // Retries, circuit breaker and timeout, so a transient blip in a dependency does not
            // become a user-visible failure.
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        return builder;
    }

    /// <summary>
    /// Configures OpenTelemetry logging, metrics and tracing, exporting over OTLP.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder ConfigureOpenTelemetry<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeFormattedMessage = true;
            logging.IncludeScopes = true;
        });

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics => metrics
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddRuntimeInstrumentation())
            .WithTracing(tracing => tracing
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                // Booking contention is only visible in the database spans, so SQL is instrumented
                // even though it is chattier than the rest.
                .AddSqlClientInstrumentation());

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    /// <summary>
    /// Adds a liveness check. Individual integrations add their own readiness checks on top.
    /// </summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The same builder, for chaining.</returns>
    public static TBuilder AddDefaultHealthChecks<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), tags: [LiveTag]);

        return builder;
    }

    /// <summary>
    /// Maps <c>/health</c> and <c>/alive</c>.
    /// </summary>
    /// <remarks>
    /// Aspire's <c>WaitFor</c> and the dashboard's health indicator both depend on these, so
    /// forgetting to call this method makes dependent resources start before the API is ready.
    /// </remarks>
    /// <param name="app">The application to map endpoints on.</param>
    /// <returns>The same application, for chaining.</returns>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        // Mapped in every environment, unlike the Aspire template, which restricts them to
        // Development. App Service needs a URL to probe, and the deployment guide tells a reviewer
        // to check one. The default response writer emits only "Healthy" or "Unhealthy", so no
        // information about dependencies is disclosed to an anonymous caller.

        // All checks must pass for the service to be ready to accept traffic.
        app.MapHealthChecks("/health");

        // Only "live" checks must pass; a failure here means the process should be restarted.
        app.MapHealthChecks("/alive", new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains(LiveTag),
        });

        return app;
    }

    /// <summary>Registers the OTLP exporter when an endpoint is configured.</summary>
    /// <typeparam name="TBuilder">The host application builder type.</typeparam>
    /// <param name="builder">The builder to configure.</param>
    /// <returns>The same builder, for chaining.</returns>
    private static TBuilder AddOpenTelemetryExporters<TBuilder>(this TBuilder builder)
        where TBuilder : IHostApplicationBuilder
    {
        var useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.AddOpenTelemetry().UseOtlpExporter();
        }

        return builder;
    }
}
