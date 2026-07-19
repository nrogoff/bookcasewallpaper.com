using BookshelfWallpaper.Api.Models;
using Microsoft.Extensions.Logging;

namespace BookshelfWallpaper.Api.Services;

public sealed class BookCoverService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly IBlobStorageService _blobStorage;

    public BookCoverService(IHttpClientFactory httpClientFactory, IBlobStorageService blobStorage)
    {
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        ArgumentNullException.ThrowIfNull(blobStorage);
        _httpClientFactory = httpClientFactory;
        _blobStorage = blobStorage;
    }

    public async Task<string?> FindAndStoreCoverAsync(BookCoverFetchJob job, ILogger logger, CancellationToken cancellationToken = default)
    {
        var coverUrl = await FindCoverImageAsync(job, logger, cancellationToken);
        if (coverUrl is null) return null;

        try
        {
            var blobName = $"covers/{job.BookId}.jpg";
            return await _blobStorage.UploadFromUrlAsync("book-covers", blobName, coverUrl, cancellationToken);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to upload cover for book {BookId}", job.BookId);
            return null;
        }
    }

    private async Task<string?> FindCoverImageAsync(BookCoverFetchJob job, ILogger logger, CancellationToken cancellationToken)
    {
        var httpClient = _httpClientFactory.CreateClient();

        // 1. Try Open Library by ASIN/ISBN
        if (!string.IsNullOrEmpty(job.Asin))
        {
            try
            {
                var olUrl = $"https://covers.openlibrary.org/b/isbn/{job.Asin}-L.jpg";
                using var head = await httpClient.SendAsync(new HttpRequestMessage(HttpMethod.Head, olUrl), cancellationToken);
                if (head.IsSuccessStatusCode && head.Content.Headers.ContentType?.MediaType?.StartsWith("image") == true)
                    return olUrl;
            }
            catch (Exception ex) { logger.LogDebug(ex, "Open Library ASIN lookup failed for {Asin}", job.Asin); }
        }

        // 2. Try Open Library search
        try
        {
            var q = Uri.EscapeDataString($"{job.Title} {job.Author}");
            var url = $"https://openlibrary.org/search.json?q={q}&fields=cover_i&limit=1";
            var json = await httpClient.GetStringAsync(url, cancellationToken);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("docs", out var docs) && docs.GetArrayLength() > 0)
            {
                var first = docs[0];
                if (first.TryGetProperty("cover_i", out var coverId) && coverId.ValueKind == System.Text.Json.JsonValueKind.Number)
                    return $"https://covers.openlibrary.org/b/id/{coverId.GetInt64()}-L.jpg";
            }
        }
        catch (Exception ex) { logger.LogDebug(ex, "Open Library search failed for {Title}", job.Title); }

        // 3. Try Google Books API
        try
        {
            var q = Uri.EscapeDataString($"{job.Title} {job.Author}");
            var key = Environment.GetEnvironmentVariable("GOOGLE_BOOKS_API_KEY");
            var url = string.IsNullOrEmpty(key)
                ? $"https://www.googleapis.com/books/v1/volumes?q={q}&maxResults=1"
                : $"https://www.googleapis.com/books/v1/volumes?q={q}&key={key}&maxResults=1";
            var json = await httpClient.GetStringAsync(url, cancellationToken);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("items", out var items) && items.GetArrayLength() > 0)
            {
                var thumbnail = items[0]
                    .GetProperty("volumeInfo")
                    .GetProperty("imageLinks")
                    .GetProperty("thumbnail")
                    .GetString();
                if (thumbnail is not null)
                    return thumbnail.Replace("zoom=1", "zoom=2").Replace("http:", "https:");
            }
        }
        catch (Exception ex) { logger.LogDebug(ex, "Google Books search failed for {Title}", job.Title); }

        return null;
    }
}
