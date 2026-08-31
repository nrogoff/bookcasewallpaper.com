using BookshelfWallpaper.Api.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BookshelfWallpaper.Api.Functions;

public sealed class SearchFunctions
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<SearchFunctions> _logger;

    public SearchFunctions(IHttpClientFactory httpClientFactory, ILogger<SearchFunctions> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    [Function("searchBooks")]
    public async Task<IActionResult> SearchBooks(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "searchBooks")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("searchBooks triggered");
        try
        {
            var q = req.Query["q"].ToString().Trim();
            if (q.Length < 2)
                return new BadRequestObjectResult(new { error = "query parameter q must be at least 2 characters" });

            var httpClient = _httpClientFactory.CreateClient();
            var url = $"https://openlibrary.org/search.json?q={Uri.EscapeDataString(q)}&fields=key,title,author_name,isbn,cover_i&limit=20";
            var json = await httpClient.GetStringAsync(url, cancellationToken);
            using var doc = JsonDocument.Parse(json);

            var results = new List<BookSearchResult>();
            if (doc.RootElement.TryGetProperty("docs", out var docs))
            {
                foreach (var item in docs.EnumerateArray())
                {
                    var title = item.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                    var author = item.TryGetProperty("author_name", out var a) && a.GetArrayLength() > 0
                        ? a[0].GetString() ?? "Unknown" : "Unknown";
                    string? coverUrl = null;
                    if (item.TryGetProperty("cover_i", out var ci) && ci.ValueKind == JsonValueKind.Number)
                        coverUrl = $"https://covers.openlibrary.org/b/id/{ci.GetInt64()}-M.jpg";

                    results.Add(new BookSearchResult
                    {
                        Title = title,
                        Author = author,
                        CoverUrl = coverUrl,
                        Source = "openlibrary",
                    });
                }
            }

            return new OkObjectResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "searchBooks error");
            return new ObjectResult(new { error = "Failed to search books" }) { StatusCode = 500 };
        }
    }
}
