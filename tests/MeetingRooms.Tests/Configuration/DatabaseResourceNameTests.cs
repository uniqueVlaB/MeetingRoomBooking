using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using MeetingRooms.Infrastructure.SQL;
using Xunit;

namespace MeetingRooms.Tests.Configuration;

/// <summary>
/// Guards the one string the AppHost and the API have to agree on.
/// </summary>
/// <remarks>
/// The Aspire SDK references project resources without their assemblies, so the AppHost cannot
/// import the constant the API reads its connection string under — it restates the literal instead.
/// If the two drift apart, the API starts and then fails to reach a database, which is a confusing
/// failure to diagnose. This test reads the AppHost source and compares, so the drift is caught at
/// test time with an obvious message.
/// </remarks>
public sealed class DatabaseResourceNameTests
{
    /// <summary>The AppHost's literal matches the constant the API reads.</summary>
    [Fact]
    public void AppHostAndInfrastructure_UseTheSameDatabaseResourceName()
    {
        var appHostSource = File.ReadAllText(
            Path.Combine(RepositoryRoot(), "src", "MeetingRooms.AppHost", "AppHost.cs"));

        var match = Regex.Match(
            appHostSource,
            """const\s+string\s+DatabaseResourceName\s*=\s*"(?<name>[^"]+)"\s*;""",
            RegexOptions.None,
            TimeSpan.FromSeconds(1));

        Assert.True(match.Success, "AppHost.cs should declare a DatabaseResourceName constant.");

        Assert.Equal(
            DependencyInjection.DatabaseResourceName,
            match.Groups["name"].Value);
    }

    /// <summary>
    /// Locates the repository root from this file's compile-time path, which is stable regardless of
    /// where the test assembly happens to be built.
    /// </summary>
    /// <param name="thisFilePath">Injected by the compiler.</param>
    /// <returns>The absolute repository root.</returns>
    private static string RepositoryRoot([CallerFilePath] string thisFilePath = "") =>
        Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(thisFilePath)!,
            "..", "..", ".."));
}
