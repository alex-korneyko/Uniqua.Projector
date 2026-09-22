namespace Uniqua.Projector.Api.IntegrationTests.Fixtures;

/// <summary>
/// Binds every integration test class to one <see cref="ApiFactory"/>, so the SQL Server container
/// is started once for the whole suite rather than once per class. The test plan asks for a
/// throwaway container "spun up per suite and torn down after" (test-plan.md § Levels), and the
/// container start is the slowest thing in this repository — paying it per class would multiply it
/// by the number of test classes for no extra coverage.
/// </summary>
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "database";
}
