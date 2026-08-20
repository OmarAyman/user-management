namespace UserManagement.UnitTests.Architecture;

/// <summary>
/// Guards the single soft-delete opt-out (ADR-0004). Reflection cannot see a method <em>call</em>, so this scans
/// the source tree instead: <c>IgnoreQueryFilters</c> may appear in exactly one file, and a new query that
/// quietly suspends the filter fails the build rather than leaking deleted users' personal data.
/// </summary>
public sealed class SoftDeleteOptOutTests
{
    /// <summary>
    /// Matches the invocation, not the word. Documentation and comments discuss the rule in several places -
    /// including the interface that declares the opt-out - and a guard that flagged prose would either fail
    /// constantly or force the rule to go undocumented.
    /// </summary>
    private const string OptOutCall = ".IgnoreQueryFilters(";

    private const string SanctionedFile = "UserRepository.cs";

    [Fact]
    public void The_query_filter_opt_out_is_called_in_exactly_one_source_file()
    {
        var sourceRoot = Path.Combine(FindRepositoryRoot(), "src");

        var offenders = Directory
            .EnumerateFiles(sourceRoot, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}Migrations{Path.DirectorySeparatorChar}", StringComparison.Ordinal))
            .Where(path => !Path.GetFileName(path).Equals(SanctionedFile, StringComparison.Ordinal))
            .Where(path => ContainsCall(path))
            .Select(Path.GetFileName)
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool ContainsCall(string path) =>
        File.ReadLines(path)
            .Select(line => line.TrimStart())
            .Where(line => !line.StartsWith("//", StringComparison.Ordinal)
                           && !line.StartsWith("*", StringComparison.Ordinal))
            .Any(line => line.Contains(OptOutCall, StringComparison.Ordinal));

    [Fact]
    public void The_sanctioned_file_still_contains_the_opt_out()
    {
        // Without this, the test above would pass trivially if the repository stopped supporting the Admin-only
        // deleted-user paths at all.
        var repository = Path.Combine(
            FindRepositoryRoot(),
            "src",
            "UserManagement.Infrastructure",
            "Persistence",
            "Repositories",
            SanctionedFile);

        Assert.True(File.Exists(repository), $"Expected to find {repository}");
        Assert.True(ContainsCall(repository), $"{SanctionedFile} no longer calls {OptOutCall}");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null)
        {
            if (directory.EnumerateFiles("UserManagement.slnx").Any()
                || directory.EnumerateFiles("UserManagement.sln").Any())
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate the repository root from the test output directory.");
    }
}
