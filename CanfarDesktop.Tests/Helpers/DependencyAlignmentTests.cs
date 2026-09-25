using System.Xml.Linq;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CanfarDesktop.Tests.Helpers;

/// <summary>
/// The libraries these tests run against are the ones the app ships.
///
/// <para>The test project compiles the app's own sources by link, and it had drifted onto older
/// packages than the app: Microsoft.Data.Sqlite 8.0.11 against the app's 10.0.x, the MVVM toolkit a
/// patch behind. Every SQLite test was exercising a SQLite no installed copy of the app contains, and
/// a green run said nothing about the one that does.</para>
/// </summary>
public class DependencyAlignmentTests
{
    [Fact]
    public void EveryPackageBothProjectsUseIsTheSameVersion()
    {
        var app = Packages("CanfarDesktop.csproj");
        var tests = Packages("CanfarDesktop.Tests/CanfarDesktop.Tests.csproj");

        var drift = tests
            .Where(t => app.TryGetValue(t.Key, out var shipped) && shipped != t.Value)
            .Select(t => $"{t.Key}: app {app[t.Key]}, tests {t.Value}")
            .ToList();

        Assert.True(drift.Count == 0,
            "the tests run against different versions than the app ships — move the test project to the " +
            "app's: " + string.Join("; ", drift));
    }

    /// <summary>
    /// The SQLite the app bundles is past CVE-2025-6965 (GHSA-2m69-gcr7-jv3q): an aggregate query could
    /// corrupt memory in every SQLite before 3.50.2, and every SQLitePCLRaw 2.1.x up to 2.1.11 bundled
    /// one. Asked of the engine itself rather than read off a package number, because the package's
    /// number is not the engine's.
    /// </summary>
    [Fact]
    public void TheBundledSqliteIsPastTheAggregateMemoryCorruptionFix()
    {
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();

        using var command = connection.CreateCommand();
        command.CommandText = "SELECT sqlite_version()";
        var version = Version.Parse((string)command.ExecuteScalar()!);

        Assert.True(version >= new Version(3, 50, 2),
            $"bundled SQLite is {version}; 3.50.2 or later is needed for CVE-2025-6965");
    }

    private static Dictionary<string, string> Packages(string project)
        => XDocument.Load(RepoFiles.PathTo(project))
            .Descendants("PackageReference")
            .Where(p => p.Attribute("Include") is not null && p.Attribute("Version") is not null)
            .ToDictionary(p => (string)p.Attribute("Include")!, p => (string)p.Attribute("Version")!,
                StringComparer.OrdinalIgnoreCase);
}
