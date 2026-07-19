using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BookshelfWallpaper.Api.Functions;

public sealed class BookFunctions
{
    private readonly ICosmosDbService _cosmos;
    private readonly ILogger<BookFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public BookFunctions(ICosmosDbService cosmos, ILogger<BookFunctions> logger)
    {
        _cosmos = cosmos;
        _logger = logger;
    }

    private static string GetUserId(HttpRequest req)
        => req.Headers.TryGetValue("x-ms-client-principal-id", out var v) && !string.IsNullOrEmpty(v) ? v.ToString() : "anonymous";

    [Function("addBook")]
    public async Task<IActionResult> AddBook(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "addBook/{shelfId}")] HttpRequest req,
        string shelfId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("addBook triggered for shelf {ShelfId}", shelfId);
        try
        {
            if (string.IsNullOrEmpty(shelfId)) return new BadRequestObjectResult(new { error = "shelfId is required" });
            var userId = GetUserId(req);
            var bookData = await JsonSerializer.DeserializeAsync<AddBookRequest>(req.Body, JsonOptions, cancellationToken);
            if (string.IsNullOrWhiteSpace(bookData?.Title))
                return new BadRequestObjectResult(new { error = "title is required" });

            var container = await _cosmos.GetBookshelvesContainerAsync(cancellationToken);
            Bookshelf shelf;
            try
            {
                var resp = await container.ReadItemAsync<Bookshelf>(shelfId, new PartitionKey(userId), cancellationToken: cancellationToken);
                shelf = resp.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new NotFoundObjectResult(new { error = "Bookshelf not found" });
            }

            var book = new Book
            {
                Id = Guid.NewGuid().ToString(),
                Title = bookData.Title.Trim(),
                Author = bookData.Author?.Trim() ?? "Unknown",
                CoverUrl = bookData.CoverUrl,
                SpineUrl = bookData.SpineUrl,
                SpineColor = bookData.SpineColor,
                SpineTextColor = bookData.SpineTextColor,
                Source = bookData.Source ?? "manual",
                AddedAt = DateTime.UtcNow.ToString("O"),
            };

            shelf.Books.Add(book);
            shelf.UpdatedAt = DateTime.UtcNow.ToString("O");
            var updated = await container.ReplaceItemAsync(shelf, shelfId, new PartitionKey(userId), cancellationToken: cancellationToken);

            // Queue cover fetch job if no cover
            if (book.CoverUrl is null && book.SpineUrl is null)
            {
                try
                {
                    var jobsContainer = await _cosmos.GetJobsContainerAsync(cancellationToken);
                    var job = new BookCoverFetchJob
                    {
                        Id = Guid.NewGuid().ToString(),
                        BookId = book.Id,
                        ShelfId = shelfId,
                        Title = book.Title,
                        Author = book.Author,
                        Status = "pending",
                        CreatedAt = DateTime.UtcNow.ToString("O"),
                    };
                    await jobsContainer.CreateItemAsync(job, new PartitionKey(job.Id), cancellationToken: cancellationToken);
                }
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to queue cover fetch job"); }
            }

            return new OkObjectResult(updated.Resource);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "addBook error");
            return new ObjectResult(new { error = "Internal server error" }) { StatusCode = 500 };
        }
    }

    [Function("removeBook")]
    public async Task<IActionResult> RemoveBook(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "removeBook/{shelfId}/{bookId}")] HttpRequest req,
        string shelfId,
        string bookId,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("removeBook triggered shelf={ShelfId} book={BookId}", shelfId, bookId);
        try
        {
            if (string.IsNullOrEmpty(shelfId) || string.IsNullOrEmpty(bookId))
                return new BadRequestObjectResult(new { error = "shelfId and bookId are required" });

            var userId = GetUserId(req);
            var container = await _cosmos.GetBookshelvesContainerAsync(cancellationToken);
            Bookshelf shelf;
            try
            {
                var resp = await container.ReadItemAsync<Bookshelf>(shelfId, new PartitionKey(userId), cancellationToken: cancellationToken);
                shelf = resp.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new NotFoundObjectResult(new { error = "Bookshelf not found" });
            }

            var originalCount = shelf.Books.Count;
            shelf.Books.RemoveAll(b => b.Id == bookId);

            if (shelf.Books.Count == originalCount)
                return new NotFoundObjectResult(new { error = "Book not found on shelf" });

            shelf.UpdatedAt = DateTime.UtcNow.ToString("O");
            var updated = await container.ReplaceItemAsync(shelf, shelfId, new PartitionKey(userId), cancellationToken: cancellationToken);
            return new OkObjectResult(updated.Resource);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "removeBook error");
            return new ObjectResult(new { error = "Internal server error" }) { StatusCode = 500 };
        }
    }
}

file sealed class AddBookRequest
{
    public string? Title { get; set; }
    public string? Author { get; set; }
    public string? CoverUrl { get; set; }
    public string? SpineUrl { get; set; }
    public string? SpineColor { get; set; }
    public string? SpineTextColor { get; set; }
    public string? Source { get; set; }
}
