using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BookshelfWallpaper.Api.Functions;

public sealed class UploadFunctions
{
    private readonly ICosmosDbService _cosmos;
    private readonly ILogger<UploadFunctions> _logger;

    public UploadFunctions(ICosmosDbService cosmos, ILogger<UploadFunctions> logger)
    {
        _cosmos = cosmos;
        _logger = logger;
    }

    private static string GetUserId(HttpRequest req)
        => req.Headers.TryGetValue("x-ms-client-principal-id", out var v) && !string.IsNullOrEmpty(v) ? v.ToString() : "anonymous";

    [Function("uploadBookList")]
    public async Task<IActionResult> UploadBookList(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "uploadBookList")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("uploadBookList triggered");
        try
        {
            var userId = GetUserId(req);
            var form = await req.ReadFormAsync(cancellationToken);
            var shelfId = form["shelfId"].ToString();
            var file = form.Files.GetFile("file");

            if (string.IsNullOrEmpty(shelfId)) return new BadRequestObjectResult(new { error = "shelfId is required" });
            if (file is null) return new BadRequestObjectResult(new { error = "file is required" });

            using var reader = new StreamReader(file.OpenReadStream());
            var text = await reader.ReadToEndAsync(cancellationToken);
            var entries = ParseBookList(text);

            if (entries.Count == 0)
                return new BadRequestObjectResult(new { error = "No valid book entries found in the uploaded file" });

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

            var existingTitles = new HashSet<string>(shelf.Books.Select(b => b.Title.ToLowerInvariant()));
            var newBooks = new List<Book>();
            var jobsContainer = await _cosmos.GetJobsContainerAsync(cancellationToken);

            foreach (var entry in entries)
            {
                if (existingTitles.Contains(entry.Title.ToLowerInvariant())) continue;

                var book = new Book
                {
                    Id = Guid.NewGuid().ToString(),
                    Title = entry.Title,
                    Author = entry.Author,
                    Source = "upload",
                    AddedAt = DateTime.UtcNow.ToString("O"),
                };
                newBooks.Add(book);

                try
                {
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
                catch (Exception ex) { _logger.LogWarning(ex, "Failed to create cover fetch job"); }
            }

            shelf.Books.AddRange(newBooks);
            shelf.UpdatedAt = DateTime.UtcNow.ToString("O");
            await container.ReplaceItemAsync(shelf, shelfId, new PartitionKey(userId), cancellationToken: cancellationToken);

            return new OkObjectResult(new AudibleSyncResult
            {
                BooksFound = entries.Count,
                BooksAdded = newBooks.Count,
                Books = newBooks,
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "uploadBookList error");
            return new ObjectResult(new { error = "Failed to process book list" }) { StatusCode = 500 };
        }
    }

    private static List<(string Title, string Author)> ParseBookList(string text)
    {
        var results = new List<(string, string)>();
        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            // CSV-style: "Title","Author"
            var csvMatch = System.Text.RegularExpressions.Regex.Match(line, @"^""([^""]+)""[,|]\s*""?([^""]*)""?$");
            if (csvMatch.Success)
            {
                results.Add((csvMatch.Groups[1].Value.Trim(), csvMatch.Groups[2].Value.Trim().NullIfEmpty() ?? "Unknown"));
                continue;
            }

            var parts = line.Split([',', '|'], 2);
            var title = parts[0].Trim();
            var author = parts.Length > 1 ? parts[1].Trim() : "Unknown";
            if (!string.IsNullOrEmpty(title)) results.Add((title, author.NullIfEmpty() ?? "Unknown"));
        }
        return results;
    }
}

file static class StringExtensions
{
    public static string? NullIfEmpty(this string s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
