using BookshelfWallpaper.Api.Models;
using Microsoft.Extensions.Logging;

namespace BookshelfWallpaper.Api.Services;

public interface IAudibleService
{
    Task<AudibleSyncResult> SyncAudibleIntoShelfAsync(string userId, string shelfId, string? marketplace, ILogger logger, CancellationToken cancellationToken = default);
    Task<string?> GetLatestShelfIdForUserAsync(string userId, CancellationToken cancellationToken = default);
    Task<bool> HasValidAudibleConnectionAsync(string userId, CancellationToken cancellationToken = default);
}
