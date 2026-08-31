using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;

namespace BookshelfWallpaper.Api.Functions;

public sealed class FetchBookCoversTimer
{
    private readonly ICosmosDbService _cosmos;
    private readonly BookCoverService _bookCoverService;
    private readonly ILogger<FetchBookCoversTimer> _logger;

    private const int MaxJobsPerRun = 20;

    public FetchBookCoversTimer(ICosmosDbService cosmos, BookCoverService bookCoverService, ILogger<FetchBookCoversTimer> logger)
    {
        _cosmos = cosmos;
        _bookCoverService = bookCoverService;
        _logger = logger;
    }

    [Function("fetchBookCovers")]
    public async Task Run([TimerTrigger("0 */30 * * * *")] TimerInfo timer, CancellationToken cancellationToken)
    {
        _logger.LogInformation("fetchBookCovers timer triggered");

        var jobsContainer = await _cosmos.GetJobsContainerAsync(cancellationToken);
        var bookshelvesContainer = await _cosmos.GetBookshelvesContainerAsync(cancellationToken);

        var query = new QueryDefinition($"SELECT TOP {MaxJobsPerRun} * FROM c WHERE c.status = 'pending' ORDER BY c.createdAt ASC");
        var pendingJobs = new List<BookCoverFetchJob>();
        using var iter = jobsContainer.GetItemQueryIterator<BookCoverFetchJob>(query);
        while (iter.HasMoreResults)
        {
            var page = await iter.ReadNextAsync(cancellationToken);
            pendingJobs.AddRange(page);
        }

        _logger.LogInformation("Processing {Count} pending cover fetch jobs", pendingJobs.Count);

        foreach (var job in pendingJobs)
        {
            try
            {
                job.Status = "processing";
                await jobsContainer.ReplaceItemAsync(job, job.Id, new PartitionKey(job.Id), cancellationToken: cancellationToken);

                var storedUrl = await _bookCoverService.FindAndStoreCoverAsync(job, _logger, cancellationToken);

                if (storedUrl is not null)
                {
                    try
                    {
                        var shelfResp = await bookshelvesContainer.ReadItemAsync<Bookshelf>(
                            job.ShelfId, new PartitionKey(job.ShelfId), cancellationToken: cancellationToken);
                        var shelf = shelfResp.Resource;
                        var bookIdx = shelf.Books.FindIndex(b => b.Id == job.BookId);
                        if (bookIdx >= 0)
                        {
                            shelf.Books[bookIdx].CoverUrl = storedUrl;
                            shelf.UpdatedAt = DateTime.UtcNow.ToString("O");
                            await bookshelvesContainer.ReplaceItemAsync(shelf, shelf.Id, new PartitionKey(shelf.UserId), cancellationToken: cancellationToken);
                        }
                    }
                    catch (Exception ex) { _logger.LogWarning(ex, "Failed to update bookshelf cover for job {JobId}", job.Id); }
                }

                job.Status = "done";
                await jobsContainer.ReplaceItemAsync(job, job.Id, new PartitionKey(job.Id), cancellationToken: cancellationToken);
                _logger.LogInformation("Processed job {JobId} for \"{Title}\"", job.Id, job.Title);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process job {JobId}", job.Id);
                try
                {
                    job.Status = "failed";
                    await jobsContainer.ReplaceItemAsync(job, job.Id, new PartitionKey(job.Id), cancellationToken: cancellationToken);
                }
                catch { /* Ignore update errors */ }
            }
        }
    }
}
