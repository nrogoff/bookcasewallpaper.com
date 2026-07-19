using Microsoft.Azure.Cosmos;

namespace BookshelfWallpaper.Api.Services;

public sealed class CosmosDbService : ICosmosDbService
{
    private readonly CosmosClient _client;
    private Database? _database;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public CosmosDbService(CosmosClient client)
    {
        ArgumentNullException.ThrowIfNull(client);
        _client = client;
    }

    private async Task<Database> GetDatabaseAsync(CancellationToken cancellationToken)
    {
        if (_database is not null) return _database;

        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_database is null)
            {
                var dbName = Environment.GetEnvironmentVariable("COSMOS_DATABASE") ?? "BookshelfWallpaper";
                var response = await _client.CreateDatabaseIfNotExistsAsync(dbName, cancellationToken: cancellationToken);
                _database = response.Database;
            }
            return _database;
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Container> GetContainerAsync(string containerId, string partitionKey, CancellationToken cancellationToken)
    {
        var db = await GetDatabaseAsync(cancellationToken);
        var response = await db.CreateContainerIfNotExistsAsync(
            new ContainerProperties(containerId, partitionKey), cancellationToken: cancellationToken);
        return response.Container;
    }

    public Task<Container> GetBookshelvesContainerAsync(CancellationToken cancellationToken = default)
        => GetContainerAsync("bookshelves", "/userId", cancellationToken);

    public Task<Container> GetJobsContainerAsync(CancellationToken cancellationToken = default)
        => GetContainerAsync("coverFetchJobs", "/id", cancellationToken);

    public Task<Container> GetAudibleConnectionsContainerAsync(CancellationToken cancellationToken = default)
        => GetContainerAsync("audibleConnections", "/userId", cancellationToken);
}
