using BookshelfWallpaper.Api.Functions;
using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using BookshelfWallpaper.Api.Tests.Helpers;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BookshelfWallpaper.Api.Tests.Functions;

public sealed class FetchBookCoversTimerTests
{
    private static FetchBookCoversTimer CreateSut(
        Mock<ICosmosDbService> cosmos,
        BookCoverService bookCoverService) =>
        new(cosmos.Object, bookCoverService, NullLogger<FetchBookCoversTimer>.Instance);

    private static BookCoverService CreateBookCoverService(
        IHttpClientFactory? http = null,
        Mock<IBlobStorageService>? blob = null)
    {
        blob ??= new Mock<IBlobStorageService>();
        return new BookCoverService(
            http ?? HttpHandlerStub.Silent(),
            blob.Object);
    }

    [Fact]
    public async Task Run_WhenNoPendingJobs_DoesNotTouchBookshelves()
    {
        var jobsContainer = CosmosFactory.Container();
        jobsContainer
            .Setup(c => c.GetItemQueryIterator<BookCoverFetchJob>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(CosmosFactory.EmptyIterator<BookCoverFetchJob>().Object);

        var bookshelvesContainer = CosmosFactory.Container();
        var cosmos = CosmosFactory.CosmosDbService(
            bookshelvesContainer: bookshelvesContainer,
            jobsContainer: jobsContainer);

        await CreateSut(cosmos, CreateBookCoverService()).Run(new TimerInfo(), CancellationToken.None);

        bookshelvesContainer.Verify(
            c => c.ReadItemAsync<Bookshelf>(It.IsAny<string>(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task Run_WithPendingJob_UpdatesJobStatusToProcessingThenDone()
    {
        var job = new BookCoverFetchJob
        {
            Id = "job-1", BookId = "book-1", ShelfId = "shelf-1",
            Title = "Dune", Author = "Herbert", Status = "pending",
        };

        var jobsContainer = SetupJobsContainerForJobs([job]);
        var shelf = new Bookshelf
        {
            Id = "shelf-1", UserId = "user-1",
            Books = [new Book { Id = "book-1", Title = "Dune" }],
        };

        var bookshelvesContainer = CosmosFactory.Container();
        bookshelvesContainer
            .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1", It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(shelf).Object);
        bookshelvesContainer
            .Setup(c => c.ReplaceItemAsync(
                It.IsAny<Bookshelf>(), "shelf-1", It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Bookshelf s, string _, PartitionKey? _, ItemRequestOptions? _, CancellationToken _) =>
                CosmosFactory.ItemResponse(s).Object);

        var cosmos = CosmosFactory.CosmosDbService(
            bookshelvesContainer: bookshelvesContainer,
            jobsContainer: jobsContainer);

        // BookCoverService that returns a cover URL via blob
        var blobMock = new Mock<IBlobStorageService>();
        blobMock
            .Setup(b => b.UploadFromUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.com/cover.jpg");

        // HTTP returns image content-type for the cover HEAD request
        var httpStub = new HttpHandlerStub(
            _ => new System.Net.Http.HttpResponseMessage(System.Net.HttpStatusCode.OK)
            {
                Content = new System.Net.Http.ByteArrayContent([])
                {
                    Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg") }
                }
            });

        var bookCoverService = CreateBookCoverService(http: httpStub.ToFactory(), blob: blobMock);

        await CreateSut(cosmos, bookCoverService).Run(new TimerInfo(), CancellationToken.None);

        // Job's status should have been updated (processing and done)
        jobsContainer.Verify(
            c => c.ReplaceItemAsync(
                It.Is<BookCoverFetchJob>(j => j.Status == "done"),
                "job-1", It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Run_WhenFindAndStoreCoverFails_MarksJobAsDone_WithoutCrash()
    {
        var job = new BookCoverFetchJob
        {
            Id = "job-2", BookId = "book-2", ShelfId = "shelf-1",
            Title = "Unknown", Author = "Unknown", Status = "pending",
        };

        var jobsContainer = SetupJobsContainerForJobs([job]);

        // BookCoverService returns null (all sources fail)
        var blobMock = new Mock<IBlobStorageService>();
        var bookCoverService = CreateBookCoverService(http: HttpHandlerStub.Silent(), blob: blobMock);

        var bookshelvesContainer = CosmosFactory.Container();
        var cosmos = CosmosFactory.CosmosDbService(
            bookshelvesContainer: bookshelvesContainer,
            jobsContainer: jobsContainer);

        await CreateSut(cosmos, bookCoverService).Run(new TimerInfo(), CancellationToken.None);

        // No bookshelf update when no cover found
        bookshelvesContainer.Verify(
            c => c.ReplaceItemAsync(
                It.IsAny<Bookshelf>(), It.IsAny<string>(), It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()),
            Times.Never);

        // Job should still be marked done
        jobsContainer.Verify(
            c => c.ReplaceItemAsync(
                It.Is<BookCoverFetchJob>(j => j.Status == "done"),
                "job-2", It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    [Fact]
    public async Task Run_WhenJobProcessingThrows_MarksJobFailed()
    {
        var job = new BookCoverFetchJob
        {
            Id = "job-3", BookId = "book-3", ShelfId = "shelf-1",
            Title = "Failing", Author = "Author", Status = "pending",
        };

        var jobsContainer = CosmosFactory.Container();
        jobsContainer
            .Setup(c => c.GetItemQueryIterator<BookCoverFetchJob>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(CosmosFactory.SinglePageIterator<BookCoverFetchJob>([job]).Object);
        // First ReplaceItemAsync (status = processing) throws
        jobsContainer
            .SetupSequence(c => c.ReplaceItemAsync(
                It.IsAny<BookCoverFetchJob>(), "job-3", It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Simulated write failure"))
            .ReturnsAsync(CosmosFactory.ItemResponse(job).Object);

        var cosmos = CosmosFactory.CosmosDbService(jobsContainer: jobsContainer);

        // Should not throw; catches exception and tries to mark failed
        await CreateSut(cosmos, CreateBookCoverService()).Run(new TimerInfo(), CancellationToken.None);

        // Second call should attempt to mark failed
        jobsContainer.Verify(
            c => c.ReplaceItemAsync(
                It.Is<BookCoverFetchJob>(j => j.Status == "failed"),
                "job-3", It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()),
            Times.AtLeastOnce);
    }

    // ── Private helpers ───────────────────────────────────────────────────────

    private static Mock<Container> SetupJobsContainerForJobs(IEnumerable<BookCoverFetchJob> jobs)
    {
        var jobsContainer = CosmosFactory.Container();
        jobsContainer
            .Setup(c => c.GetItemQueryIterator<BookCoverFetchJob>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(CosmosFactory.SinglePageIterator<BookCoverFetchJob>(jobs).Object);
        jobsContainer
            .Setup(c => c.ReplaceItemAsync(
                It.IsAny<BookCoverFetchJob>(), It.IsAny<string>(), It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((BookCoverFetchJob j, string _, PartitionKey? _, ItemRequestOptions? _, CancellationToken _) =>
                CosmosFactory.ItemResponse(j).Object);
        return jobsContainer;
    }
}
