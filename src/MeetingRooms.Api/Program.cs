using MeetingRooms.Api.Config;
using MeetingRooms.Api.Hubs;
using MeetingRooms.Api.Infrastructure;
using MeetingRooms.Core;
using MeetingRooms.Infrastructure.SQL;
using MeetingRooms.ServiceDefaults;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Telemetry, health checks, service discovery and HTTP resilience.
builder.AddServiceDefaults();

// Domain services, then the persistence they depend on, then the HTTP surface.
builder.Services.AddCoreServices();
builder.AddSqlInfrastructure();
builder.AddApiServices();
builder.AddAuth();

var app = builder.Build();

// First in the pipeline on purpose: anything that throws further down should leave as RFC 9457
// problem details, not as an HTML error page or a bare 500.
app.UseExceptionHandler();
app.UseStatusCodePages();

app.UseHttpsRedirection();

// Before authentication, so that a rejected pre-flight still carries CORS headers and the browser
// reports the real reason rather than an opaque network error.
app.UseCors(ApiConfiguration.CorsPolicyName);

app.UseAuthentication();
app.UseAuthorization();

// /health and /alive. Aspire's WaitFor depends on these, so a dependent resource does not start
// before the API can serve it.
app.MapDefaultEndpoints();

app.MapControllers();
app.MapHub<BookingHub>(BookingHub.Route);

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

// Applies migrations and seeds, both gated by configuration. See DatabaseInitializer for why
// migrating at start-up is the chosen approach here.
await DatabaseInitializer.InitializeAsync(app.Services, app.Configuration);

await app.RunAsync();

/// <summary>
/// Entry-point marker.
/// </summary>
/// <remarks>
/// Declared explicitly so the integration tests can boot this exact pipeline through
/// <c>WebApplicationFactory&lt;Program&gt;</c>. The concurrency test has to exercise the real
/// routing, authentication and model binding, because the guarantee it checks is about what a
/// client observes, not about what a service returns.
/// </remarks>
public partial class Program
{
    /// <summary>Prevents the compiler generating a public parameterless constructor.</summary>
    protected Program()
    {
    }
}
