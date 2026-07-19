using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace BookshelfWallpaper.Api.Functions;

public sealed class BookshelfFunctions
{
    private readonly ICosmosDbService _cosmos;
    private readonly ILogger<BookshelfFunctions> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public BookshelfFunctions(ICosmosDbService cosmos, ILogger<BookshelfFunctions> logger)
    {
        _cosmos = cosmos;
        _logger = logger;
    }

    private static string GetUserId(HttpRequest req)
        => req.Headers.TryGetValue("x-ms-client-principal-id", out var v) && !string.IsNullOrEmpty(v) ? v.ToString() : "anonymous";

    [Function("getBookshelves")]
    public async Task<IActionResult> GetBookshelves(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "getBookshelves")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("getBookshelves triggered");
        try
        {
            var userId = GetUserId(req);
            var container = await _cosmos.GetBookshelvesContainerAsync(cancellationToken);
            var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId ORDER BY c.updatedAt DESC")
                .WithParameter("@userId", userId);
            var results = new List<Bookshelf>();
            using var iter = container.GetItemQueryIterator<Bookshelf>(query);
            while (iter.HasMoreResults)
            {
                var page = await iter.ReadNextAsync(cancellationToken);
                results.AddRange(page);
            }
            return new OkObjectResult(results);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "getBookshelves error");
            return new ObjectResult(new { error = "Internal server error" }) { StatusCode = 500 };
        }
    }

    [Function("getBookshelf")]
    public async Task<IActionResult> GetBookshelf(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "getBookshelf/{id}")] HttpRequest req,
        string id,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("getBookshelf triggered for {Id}", id);
        try
        {
            if (string.IsNullOrEmpty(id)) return new BadRequestObjectResult(new { error = "id is required" });
            var userId = GetUserId(req);
            var container = await _cosmos.GetBookshelvesContainerAsync(cancellationToken);
            try
            {
                var resp = await container.ReadItemAsync<Bookshelf>(id, new PartitionKey(userId), cancellationToken: cancellationToken);
                return new OkObjectResult(resp.Resource);
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new NotFoundObjectResult(new { error = "Bookshelf not found" });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "getBookshelf error");
            return new ObjectResult(new { error = "Internal server error" }) { StatusCode = 500 };
        }
    }

    [Function("createBookshelf")]
    public async Task<IActionResult> CreateBookshelf(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "createBookshelf")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("createBookshelf triggered");
        try
        {
            var userId = GetUserId(req);
            var body = await JsonSerializer.DeserializeAsync<CreateBookshelfRequest>(req.Body, JsonOptions, cancellationToken);
            if (string.IsNullOrWhiteSpace(body?.Name))
                return new BadRequestObjectResult(new { error = "name is required" });

            var now = DateTime.UtcNow.ToString("O");
            var shelf = new Bookshelf
            {
                Id = Guid.NewGuid().ToString(),
                UserId = userId,
                Name = body.Name.Trim(),
                Books = [],
                Settings = body.Settings ?? new BookshelfSettings(),
                CreatedAt = now,
                UpdatedAt = now,
            };

            var container = await _cosmos.GetBookshelvesContainerAsync(cancellationToken);
            var resp = await container.CreateItemAsync(shelf, new PartitionKey(userId), cancellationToken: cancellationToken);
            return new ObjectResult(resp.Resource) { StatusCode = 201 };
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing request body" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "createBookshelf error");
            return new ObjectResult(new { error = "Internal server error" }) { StatusCode = 500 };
        }
    }

    [Function("updateBookshelf")]
    public async Task<IActionResult> UpdateBookshelf(
        [HttpTrigger(AuthorizationLevel.Anonymous, "patch", Route = "updateBookshelf/{id}")] HttpRequest req,
        string id,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("updateBookshelf triggered for {Id}", id);
        try
        {
            if (string.IsNullOrEmpty(id)) return new BadRequestObjectResult(new { error = "id is required" });
            var userId = GetUserId(req);
            var updates = await JsonSerializer.DeserializeAsync<BookshelfUpdate>(req.Body, JsonOptions, cancellationToken);

            var container = await _cosmos.GetBookshelvesContainerAsync(cancellationToken);
            Bookshelf existing;
            try
            {
                var resp = await container.ReadItemAsync<Bookshelf>(id, new PartitionKey(userId), cancellationToken: cancellationToken);
                existing = resp.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new NotFoundObjectResult(new { error = "Bookshelf not found" });
            }

            if (updates?.Name is not null) existing.Name = updates.Name;
            if (updates?.Settings is not null) existing.Settings = updates.Settings;
            existing.UpdatedAt = DateTime.UtcNow.ToString("O");

            var updated = await container.ReplaceItemAsync(existing, id, new PartitionKey(userId), cancellationToken: cancellationToken);
            return new OkObjectResult(updated.Resource);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "updateBookshelf error");
            return new ObjectResult(new { error = "Internal server error" }) { StatusCode = 500 };
        }
    }

    [Function("deleteBookshelf")]
    public async Task<IActionResult> DeleteBookshelf(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "deleteBookshelf/{id}")] HttpRequest req,
        string id,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("deleteBookshelf triggered for {Id}", id);
        try
        {
            if (string.IsNullOrEmpty(id)) return new BadRequestObjectResult(new { error = "id is required" });
            var userId = GetUserId(req);
            var container = await _cosmos.GetBookshelvesContainerAsync(cancellationToken);
            await container.DeleteItemAsync<Bookshelf>(id, new PartitionKey(userId), cancellationToken: cancellationToken);
            return new StatusCodeResult(204);
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return new StatusCodeResult(204); // Idempotent delete
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "deleteBookshelf error");
            return new ObjectResult(new { error = "Internal server error" }) { StatusCode = 500 };
        }
    }
}

file sealed class CreateBookshelfRequest
{
    public string? Name { get; set; }
    public BookshelfSettings? Settings { get; set; }
}

file sealed class BookshelfUpdate
{
    public string? Name { get; set; }
    public BookshelfSettings? Settings { get; set; }
}
