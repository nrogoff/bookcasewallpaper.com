using Microsoft.Azure.Cosmos;

namespace BookshelfWallpaper.Api.Services;

public interface ICosmosDbService
{
    Task<Container> GetBookshelvesContainerAsync(CancellationToken cancellationToken = default);
    Task<Container> GetJobsContainerAsync(CancellationToken cancellationToken = default);
    Task<Container> GetAudibleConnectionsContainerAsync(CancellationToken cancellationToken = default);
}
