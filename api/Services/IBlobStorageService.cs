namespace BookshelfWallpaper.Api.Services;

public interface IBlobStorageService
{
    Task<string> UploadBufferAsync(string containerName, string blobName, byte[] data, string contentType, CancellationToken cancellationToken = default);
    Task<string> UploadFromUrlAsync(string containerName, string blobName, string sourceUrl, CancellationToken cancellationToken = default);
    string GetBlobUrl(string containerName, string blobName);
}
