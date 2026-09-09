using MeetingRooms.AppHost;
using Microsoft.Extensions.Configuration;

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
// A container is the default, but "UseLocalSql=true" points the API at an existing SQL Server or
// LocalDB instead, so the solution still runs when Docker is not available.
var useLocalSql = builder.Configuration.GetValue("UseLocalSql", defaultValue: false);

IResourceBuilder<IResourceWithConnectionString> database;
IResourceBuilder<IResource>? databaseToWaitFor = null;

if (useLocalSql)
{
    // Reads ConnectionStrings:meetingrooms-db from this project's configuration or user secrets.
    database = builder.AddConnectionString(DatabaseResourceName);
}
else
{
    var sqlDatabase = builder
        .AddSqlServer("sql")
        // Survives a restart of the AppHost, so seeded rooms and accounts are not lost between runs.
        .WithDataVolume("meetingrooms-sql-data")
        .AddDatabase(DatabaseResourceName);

    database = sqlDatabase;
    databaseToWaitFor = sqlDatabase;
}

// ── API ───────────────────────────────────────────────────────────────────────────────────────

var api = builder
    .AddProject<Projects.MeetingRooms_Api>("meetingrooms-api")
    .WithReference(database)
    .WithEnvironment("Jwt__SigningKey", jwt.SigningKey)
    .WithEnvironment("Jwt__Issuer", jwt.Issuer)
    .WithEnvironment("Jwt__Audience", jwt.Audience)
    .WithEnvironment("Seed__Admin__Password", seed.AdminPassword)
    .WithEnvironment("Seed__User__Password", seed.UserPassword);

if (databaseToWaitFor is not null)
{
    // Only meaningful for the container: an external connection string has nothing to start.
    api = api.WaitFor(databaseToWaitFor);
}

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
