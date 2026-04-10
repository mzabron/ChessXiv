namespace ChessXiv.IntegrationTests.Infrastructure;

[CollectionDefinition(Name)]
public class PostgresCollection : ICollectionFixture<PostgresTestFixture>
{
    public const string Name = "postgres-collection";
}
