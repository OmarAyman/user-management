namespace UserManagement.IntegrationTests.TestSupport;

/// <summary>
/// The seeded demo accounts. Same values the README documents, so a test failure and a manual sign-in are
/// talking about the same accounts.
/// </summary>
public static class DemoCredentials
{
    public const string AdminUsername = "admin";
    public const string AdminPassword = "Admin@123456";

    public const string UserUsername = "jdoe";
    public const string UserPassword = "User@1234567";

    public const string ReadOnlyUsername = "readonly";
    public const string ReadOnlyPassword = "ReadOnly@1234";
}

/// <summary>Definition for the collection that shares one API host and one database container.</summary>
[CollectionDefinition(Name)]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>
{
    public const string Name = "api";
}
