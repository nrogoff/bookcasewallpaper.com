using BookshelfWallpaper.Api.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BookshelfWallpaper.Api.Services;

public sealed class AudibleSyncException : Exception
{
    public int Status { get; }

    public AudibleSyncException(int status, string message) : base(message)
    {
        Status = status;
    }
}

internal sealed class AudibleLibraryItem
{
    [JsonPropertyName("asin")] public string Asin { get; set; } = string.Empty;
    [JsonPropertyName("title")] public string Title { get; set; } = string.Empty;
    [JsonPropertyName("authors")] public List<AudibleAuthor>? Authors { get; set; }
    [JsonPropertyName("product_images")] public Dictionary<string, string>? ProductImages { get; set; }
}

internal sealed class AudibleAuthor
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
}

internal sealed class AmazonTokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string? RefreshToken { get; set; }
    [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    [JsonPropertyName("expires_in")] public int? ExpiresIn { get; set; }
}

public sealed class AudibleService : IAudibleService
{
    private readonly ICosmosDbService _cosmos;
    private readonly IHttpClientFactory _httpClientFactory;

    private static readonly string[] AllMarketplaces = ["US", "UK", "DE", "FR", "AU", "CA", "JP", "IT", "ES", "IN"];

    private static readonly Dictionary<string, string> AudibleApiHosts = new()
    {
        ["US"] = "api.audible.com",
        ["UK"] = "api.audible.co.uk",
        ["DE"] = "api.audible.de",
        ["FR"] = "api.audible.fr",
        ["AU"] = "api.audible.com.au",
        ["CA"] = "api.audible.ca",
        ["JP"] = "api.audible.co.jp",
        ["IT"] = "api.audible.it",
        ["ES"] = "api.audible.es",
        ["IN"] = "api.audible.in",
    };

    public AudibleService(ICosmosDbService cosmos, IHttpClientFactory httpClientFactory)
    {
        ArgumentNullException.ThrowIfNull(cosmos);
        ArgumentNullException.ThrowIfNull(httpClientFactory);
        _cosmos = cosmos;
        _httpClientFactory = httpClientFactory;
    }

    public async Task<AudibleSyncResult> SyncAudibleIntoShelfAsync(
        string userId, string shelfId, string? marketplace, ILogger logger, CancellationToken cancellationToken = default)
    {
        var bookshelvesContainer = await _cosmos.GetBookshelvesContainerAsync(cancellationToken);
        Bookshelf? shelf;
        try
        {
            var resp = await bookshelvesContainer.ReadItemAsync<Bookshelf>(shelfId, new PartitionKey(userId), cancellationToken: cancellationToken);
            shelf = resp.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            shelf = null;
        }

        if (shelf is null) throw new AudibleSyncException(404, "Bookshelf not found");

        var connection = await GetAudibleConnectionForUserAsync(userId, cancellationToken);
        var accessToken = connection?.AccessToken ?? Environment.GetEnvironmentVariable("AUDIBLE_ACCESS_TOKEN");
        if (string.IsNullOrEmpty(accessToken))
            throw new AudibleSyncException(428, "Audible account not connected. Please connect your Audible account first.");

        if (connection is not null && IsExpiring(connection.ExpiresAt))
        {
            var refreshed = await RefreshAudibleAccessTokenAsync(connection, logger, cancellationToken);
            if (refreshed is not null)
            {
                connection = refreshed;
                accessToken = refreshed.AccessToken;
                logger.LogInformation("Refreshed Audible access token before sync");
            }
        }

        var preferredMarketplace = marketplace ?? connection?.Marketplace ?? "UK";
        var marketplaceOrder = GetMarketplaceFallbackOrder(preferredMarketplace);

        List<AudibleLibraryItem>? items = null;
        string effectiveMarketplace = preferredMarketplace;
        bool refreshedOnAuthFailure = false;

        var httpClient = _httpClientFactory.CreateClient();

        foreach (var candidate in marketplaceOrder)
        {
            try
            {
                items = await FetchAudibleLibraryAsync(httpClient, accessToken, connection?.TokenType, candidate, cancellationToken);
                effectiveMarketplace = candidate;
                break;
            }
            catch (HttpRequestException ex) when (ex.StatusCode is System.Net.HttpStatusCode.Unauthorized or System.Net.HttpStatusCode.Forbidden)
            {
                if (!refreshedOnAuthFailure && connection?.RefreshToken is not null)
                {
                    refreshedOnAuthFailure = true;
                    var refreshed = await RefreshAudibleAccessTokenAsync(connection, logger, cancellationToken);
                    if (refreshed is not null)
                    {
                        connection = refreshed;
                        accessToken = refreshed.AccessToken;
                        try
                        {
                            items = await FetchAudibleLibraryAsync(httpClient, accessToken, connection.TokenType, candidate, cancellationToken);
                            effectiveMarketplace = candidate;
                            break;
                        }
                        catch { /* continue to next marketplace */ }
                    }
                }
                // Try next marketplace
            }
        }

        if (items is null)
            throw new AudibleSyncException(403, $"Audible denied library access. Please reconnect your Audible account.");

        // Update marketplace if changed
        if (connection is not null && connection.Marketplace != effectiveMarketplace)
        {
            try
            {
                var connectionsContainer = await _cosmos.GetAudibleConnectionsContainerAsync(cancellationToken);
                connection.Marketplace = effectiveMarketplace;
                connection.UpdatedAt = DateTime.UtcNow.ToString("O");
                await connectionsContainer.UpsertItemAsync(connection, new PartitionKey(connection.UserId), cancellationToken: cancellationToken);
            }
            catch (Exception ex) { logger.LogWarning(ex, "Failed to persist detected Audible marketplace"); }
        }

        var existingAsins = new HashSet<string>(shelf.Books.Where(b => b.Asin is not null).Select(b => b.Asin!));
        var newBooks = new List<Book>();
        var jobsContainer = await _cosmos.GetJobsContainerAsync(cancellationToken);

        foreach (var item in items)
        {
            if (!string.IsNullOrEmpty(item.Asin) && existingAsins.Contains(item.Asin)) continue;

            var book = new Book
            {
                Id = Guid.NewGuid().ToString(),
                Title = item.Title,
                Author = item.Authors?.FirstOrDefault()?.Name ?? "Unknown",
                CoverUrl = item.ProductImages?.GetValueOrDefault("500") ?? item.ProductImages?.GetValueOrDefault("330"),
                Source = "audible",
                Asin = item.Asin,
                AddedAt = DateTime.UtcNow.ToString("O"),
            };

            newBooks.Add(book);

            if (book.SpineUrl is null)
            {
                var job = new BookCoverFetchJob
                {
                    Id = Guid.NewGuid().ToString(),
                    BookId = book.Id,
                    ShelfId = shelfId,
                    Title = book.Title,
                    Author = book.Author,
                    Asin = book.Asin,
                    Status = "pending",
                    CreatedAt = DateTime.UtcNow.ToString("O"),
                };
                try { await jobsContainer.CreateItemAsync(job, new PartitionKey(job.Id), cancellationToken: cancellationToken); }
                catch (Exception ex) { logger.LogWarning(ex, "Failed to create cover fetch job"); }
            }
        }

        shelf.Books.AddRange(newBooks);
        shelf.UpdatedAt = DateTime.UtcNow.ToString("O");
        await bookshelvesContainer.ReplaceItemAsync(shelf, shelf.Id, new PartitionKey(userId), cancellationToken: cancellationToken);

        return new AudibleSyncResult
        {
            BooksFound = items.Count,
            BooksAdded = newBooks.Count,
            Books = newBooks,
        };
    }

    public async Task<string?> GetLatestShelfIdForUserAsync(string userId, CancellationToken cancellationToken = default)
    {
        var container = await _cosmos.GetBookshelvesContainerAsync(cancellationToken);
        var query = new QueryDefinition("SELECT TOP 1 c.id FROM c WHERE c.userId = @userId ORDER BY c.updatedAt DESC")
            .WithParameter("@userId", userId);
        using var iter = container.GetItemQueryIterator<dynamic>(query);
        if (iter.HasMoreResults)
        {
            var page = await iter.ReadNextAsync(cancellationToken);
            var first = page.FirstOrDefault();
            if (first is not null) return (string?)first.id;
        }
        return null;
    }

    public async Task<bool> HasValidAudibleConnectionAsync(string userId, CancellationToken cancellationToken = default)
    {
        var connection = await GetAudibleConnectionForUserAsync(userId, cancellationToken);
        if (connection?.AccessToken is null) return false;
        if (connection.ExpiresAt is null) return true;
        return DateTime.TryParse(connection.ExpiresAt, out var exp) && exp > DateTime.UtcNow;
    }

    private async Task<AudibleConnection?> GetAudibleConnectionForUserAsync(string userId, CancellationToken cancellationToken)
    {
        var container = await _cosmos.GetAudibleConnectionsContainerAsync(cancellationToken);
        try
        {
            var resp = await container.ReadItemAsync<AudibleConnection>(userId, new PartitionKey(userId), cancellationToken: cancellationToken);
            return resp.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    private async Task<List<AudibleLibraryItem>> FetchAudibleLibraryAsync(
        HttpClient httpClient, string accessToken, string? tokenType, string marketplace, CancellationToken cancellationToken)
    {
        var host = AudibleApiHosts.GetValueOrDefault(marketplace, "api.audible.co.uk");
        var audibleClientId = Environment.GetEnvironmentVariable("AUDIBLE_CLIENT_ID") ?? "0";
        var url = $"https://{host}/1.0/library?response_groups=product_details,product_desc&num_results=1000";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.TryAddWithoutValidation("Authorization", $"{tokenType ?? "Bearer"} {accessToken}");
        request.Headers.TryAddWithoutValidation("client-id", audibleClientId);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();

        var json = await response.Content.ReadAsStringAsync(cancellationToken);
        var doc = JsonDocument.Parse(json);
        var items = new List<AudibleLibraryItem>();
        if (doc.RootElement.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            items = JsonSerializer.Deserialize<List<AudibleLibraryItem>>(itemsEl.GetRawText()) ?? [];
        }
        return items;
    }

    private async Task<AudibleConnection?> RefreshAudibleAccessTokenAsync(
        AudibleConnection connection, ILogger logger, CancellationToken cancellationToken)
    {
        var clientId = Environment.GetEnvironmentVariable("AMAZON_CLIENT_ID");
        var clientSecret = Environment.GetEnvironmentVariable("AMAZON_CLIENT_SECRET");
        if (string.IsNullOrEmpty(clientId) || string.IsNullOrEmpty(clientSecret) || string.IsNullOrEmpty(connection.RefreshToken))
            return null;

        try
        {
            var httpClient = _httpClientFactory.CreateClient();
            var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = connection.RefreshToken,
                ["client_id"] = clientId,
                ["client_secret"] = clientSecret,
            });

            using var response = await httpClient.PostAsync("https://api.amazon.com/auth/o2/token", body, cancellationToken);
            response.EnsureSuccessStatusCode();

            var tokenResp = await JsonSerializer.DeserializeAsync<AmazonTokenResponse>(
                await response.Content.ReadAsStreamAsync(cancellationToken),
                cancellationToken: cancellationToken) ?? throw new InvalidOperationException("Empty token response");

            var refreshed = new AudibleConnection
            {
                Id = connection.Id,
                UserId = connection.UserId,
                AccessToken = tokenResp.AccessToken,
                RefreshToken = tokenResp.RefreshToken ?? connection.RefreshToken,
                TokenType = tokenResp.TokenType ?? connection.TokenType,
                ExpiresAt = tokenResp.ExpiresIn.HasValue
                    ? DateTime.UtcNow.AddSeconds(tokenResp.ExpiresIn.Value).ToString("O")
                    : connection.ExpiresAt,
                Marketplace = connection.Marketplace,
                CreatedAt = connection.CreatedAt,
                UpdatedAt = DateTime.UtcNow.ToString("O"),
            };

            var container = await _cosmos.GetAudibleConnectionsContainerAsync(cancellationToken);
            await container.UpsertItemAsync(refreshed, new PartitionKey(refreshed.UserId), cancellationToken: cancellationToken);
            return refreshed;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to refresh Audible access token");
            return null;
        }
    }

    private static bool IsExpiring(string? expiresAt)
    {
        if (string.IsNullOrEmpty(expiresAt)) return false;
        return DateTime.TryParse(expiresAt, out var exp) && exp <= DateTime.UtcNow.AddMinutes(1);
    }

    private static IEnumerable<string> GetMarketplaceFallbackOrder(string preferred)
        => new[] { preferred }.Concat(AllMarketplaces.Where(m => m != preferred));
}
