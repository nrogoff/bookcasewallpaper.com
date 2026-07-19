using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace BookshelfWallpaper.Api.Services;

public sealed class BlobStorageService : IBlobStorageService
{
    private readonly BlobServiceClient _client;
    private readonly IHttpClientFactory _httpClientFactory;

    public BlobStorageService(BlobServiceClient client, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(client);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _client = client;
        _httpClientFactory = httpClientFactory;
    }

    private async Task<BlobContainerClient> GetContainerClientAsync(string containerName, CancellationToken cancellationToken)
    {
        var containerClient = _client.GetBlobContainerClient(containerName);
        await containerClient.CreateIfNotExistsAsync(PublicAccessType.Blob, cancellationToken: cancellationToken);
        return containerClient;
    }

    public async Task<string> UploadBufferAsync(string containerName, string blobName, byte[] data, string contentType, CancellationToken cancellationToken = default)
    {
        var container = await GetContainerClientAsync(containerName, cancellationToken);
        var blobClient = container.GetBlobClient(blobName);
        using var stream = new MemoryStream(data);
        await blobClient.UploadAsync(stream, new BlobHttpHeaders { ContentType = contentType }, cancellationToken: cancellationToken);
        return blobClient.Uri.ToString();
    }

    public async Task<string> UploadFromUrlAsync(string containerName, string blobName, string sourceUrl, CancellationToken cancellationToken = default)
    {
        var container = await GetContainerClientAsync(containerName, cancellationToken);
        var blobClient = container.GetBlobClient(blobName);

        var httpClient = _httpClientFactory.CreateClient();
        var data = await httpClient.GetByteArrayAsync(sourceUrl, cancellationToken);
        using var stream = new MemoryStream(data);
        await blobClient.UploadAsync(stream, cancellationToken: cancellationToken);
        return blobClient.Uri.ToString();
    }

    public string GetBlobUrl(string containerName, string blobName)
    {
        return _client.GetBlobContainerClient(containerName).GetBlobClient(blobName).Uri.ToString();
    }
}
