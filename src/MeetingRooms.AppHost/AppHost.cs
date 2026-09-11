using System.Diagnostics;
using MeetingRooms.AppHost;
using Microsoft.Extensions.Diagnostics.HealthChecks;

// Must match MeetingRooms.Infrastructure.SQL.DependencyInjection.DatabaseResourceName, which is what
// the API reads its connection string under. It is repeated rather than shared because the Aspire
// SDK references project resources without their assemblies, so there is nothing to import here.
// DatabaseResourceNameTests asserts the two stay equal.
const string DatabaseResourceName = "meetingrooms-db";

var builder = DistributedApplication.CreateBuilder(args);

// ── Parameters (real values live in this project's user secrets) ──────────────────────────────

var jwt = builder.AddJwtParameters();
var seed = builder.AddSeedParameters();

// ── Database ──────────────────────────────────────────────────────────────────────────────────

// SQL Server rather than any other engine because the deployment target is Azure SQL, and the
// concurrency guarantee is expressed as a SQL Server filtered unique index. Developing against a
// different engine would mean the one behaviour this system must get right was never exercised
// locally.
//
// Always a local SQL Server instance (LocalDB by default -- see appsettings.json), never a
// Docker-hosted container. A container's data volume outlives the container itself: if a crashed
// or force-killed run leaves the volume behind, and Aspire's generated "sa" password parameter has
// since changed, the next container boots cleanly but every connection then fails with error 18456
// "password did not match" -- confusing to diagnose, and easy to trigger from nothing more than an
// unclean shutdown. A local instance has no such lifecycle to manage, and it is what the design-time
// migration tooling (AppDbContextFactory) already assumes.
var database = builder.AddConnectionString(DatabaseResourceName);

// ── API ───────────────────────────────────────────────────────────────────────────────────────

var api = builder
    .AddProject<Projects.MeetingRooms_Api>("meetingrooms-api")
    .WithReference(database)
    .WithEnvironment("Jwt__SigningKey", jwt.SigningKey)
    .WithEnvironment("Jwt__Issuer", jwt.Issuer)
    .WithEnvironment("Jwt__Audience", jwt.Audience)
    .WithEnvironment("Seed__Admin__Password", seed.AdminPassword)
    .WithEnvironment("Seed__User__Password", seed.UserPassword)
    // Gives the dashboard's Health column a real status instead of "Unknown", and is what the
    // Scalar shortcut below waits on before it enables itself. /health is mapped in every
    // environment (see MeetingRooms.ServiceDefaults), so this works the same way it will in Azure.
    .WithHttpHealthCheck("/health");

#if DEBUG
// A one-click way to open the API's Scalar documentation from the Aspire dashboard, instead of
// hunting down whatever HTTPS port Aspire assigned this run. Enabled only once the health check
// above reports healthy, so clicking it before start-up doesn't open a page that isn't serving
// anything yet.
api.WithCommand(
    name: "scalar-api-docs",
    displayName: "Scalar API Docs",
    executeCommand: async _ =>
    {
        try
        {
            var url = $"{api.GetEndpoint("https").Url}/scalar/v1";
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            return new ExecuteCommandResult { Success = true };
        }
        catch (Exception exception)
        {
            return new ExecuteCommandResult { Success = false, Message = exception.ToString() };
        }
    },
    commandOptions: new CommandOptions
    {
        UpdateState = context => context.ResourceSnapshot.HealthStatus == HealthStatus.Healthy
            ? ResourceCommandState.Enabled
            : ResourceCommandState.Disabled,
        IconName = "Document",
        IconVariant = IconVariant.Filled,
    });
#endif

// ── Angular client ────────────────────────────────────────────────────────────────────────────

// API_URL is read by client/proxy.conf.js, so the dev-server proxy follows whatever port Aspire
// assigned the API. The reference project hardcoded that port in four separate files and broke
// whenever one of them moved.
builder
    .AddJavaScriptApp("meetingrooms-client", "../../client", runScriptName: "start")
    // Runs "npm install" before the dev server starts, so a fresh clone needs no manual step.
    .WithNpm()
    .WithReference(api)
    .WaitFor(api)
    .WithEnvironment("API_URL", api.GetEndpoint("https"))
    // The Angular dev server's port is fixed in angular.json; 4300 is what a browser uses.
    .WithHttpEndpoint(name: "client", targetPort: 4200, port: 4300)
    .WithExternalHttpEndpoints();

await builder.Build().RunAsync();
