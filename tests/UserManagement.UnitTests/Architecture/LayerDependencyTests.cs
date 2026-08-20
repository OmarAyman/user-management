using System.Reflection;
using UserManagement.Application.Common.Abstractions;
using UserManagement.Domain.Entities;
using UserManagement.Infrastructure.Persistence;

namespace UserManagement.UnitTests.Architecture;

/// <summary>
/// Makes the layering rules executable. "Domain must not depend on Infrastructure" holds until someone adds a
/// convenient <c>using</c>; a review catches that inconsistently, and a test catches it every time.
/// </summary>
public sealed class LayerDependencyTests
{
    private static readonly Assembly Domain = typeof(User).Assembly;
    private static readonly Assembly Application = typeof(IUserRepository).Assembly;
    private static readonly Assembly Infrastructure = typeof(ApplicationDbContext).Assembly;

    [Fact]
    public void Domain_depends_on_nothing_but_the_runtime()
    {
        var forbidden = References(Domain)
            .Where(name => name.StartsWith("UserManagement", StringComparison.Ordinal)
                           || name.StartsWith("Microsoft.", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Application_does_not_depend_on_AspNetCore_or_EntityFrameworkCore()
    {
        var forbidden = References(Application)
            .Where(name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal)
                           || name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal))
            .ToList();

        // The ports live in Application and their HttpContext-backed adapters live in the API layer. That is
        // what keeps use cases unit-testable without a web host.
        Assert.Empty(forbidden);
    }

    [Fact]
    public void Application_depends_on_Domain_only_among_solution_assemblies()
    {
        var solutionReferences = References(Application)
            .Where(name => name.StartsWith("UserManagement", StringComparison.Ordinal))
            .ToList();

        Assert.Equal(["UserManagement.Domain"], solutionReferences);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_the_Api()
    {
        var forbidden = References(Infrastructure)
            .Where(name => name.Equals("UserManagement.Api", StringComparison.Ordinal))
            .ToList();

        Assert.Empty(forbidden);
    }

    private static IEnumerable<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? string.Empty)
            .Where(name => !name.StartsWith("System", StringComparison.Ordinal)
                           && !name.Equals("netstandard", StringComparison.Ordinal))
            .OrderBy(name => name, StringComparer.Ordinal);
}
