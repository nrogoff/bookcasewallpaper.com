using BookshelfWallpaper.Api.Functions;
using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using BookshelfWallpaper.Api.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BookshelfWallpaper.Api.Tests.Functions;

public sealed class UploadFunctionsTests
{
    private static UploadFunctions CreateSut(Mock<ICosmosDbService>? cosmos = null) =>
        new(cosmos?.Object ?? CosmosFactory.CosmosDbService().Object,
            NullLogger<UploadFunctions>.Instance);

    // ── UploadBookList – boundary tests ───────────────────────────────────────

    [Fact]
    public async Task UploadBookList_WithMissingShelfId_ReturnsBadRequest()
    {
        var result = await CreateSut().UploadBookList(
            RequestFactory.WithFormFileNoShelf(fileContent: "Title,Author"),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UploadBookList_WithNoFile_ReturnsBadRequest()
    {
        var result = await CreateSut().UploadBookList(
            RequestFactory.WithFormNoFile(shelfId: "shelf-1"),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UploadBookList_WithEmptyFile_ReturnsBadRequest()
    {
        var result = await CreateSut().UploadBookList(
            RequestFactory.WithFormFile(shelfId: "shelf-1", fileContent: ""),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task UploadBookList_WhenShelfNotFound_Returns404()
    {
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosFactory.NotFound());

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));

        var result = await sut.UploadBookList(
            RequestFactory.WithFormFile(shelfId: "missing-shelf", fileContent: "Dune,Herbert"),
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task UploadBookList_WithValidCsvFile_AddsNewBooksAndReturnsCount()
    {
        var shelf = new Bookshelf { Id = "shelf-1", UserId = "user-1", Name = "Test" };
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(shelf).Object);
        containerMock
            .Setup(c => c.ReplaceItemAsync(
                It.IsAny<Bookshelf>(), "shelf-1", It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Bookshelf s, string _, PartitionKey? _, ItemRequestOptions? _, CancellationToken _) =>
                CosmosFactory.ItemResponse(s).Object);

        var jobsMock = CosmosFactory.Container();
        jobsMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<BookCoverFetchJob>(), It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(new BookCoverFetchJob()).Object);

        var sut = CreateSut(CosmosFactory.CosmosDbService(
            bookshelvesContainer: containerMock, jobsContainer: jobsMock));

        var csv = "Dune,Frank Herbert\nFoundation,Isaac Asimov";
        var result = await sut.UploadBookList(
            RequestFactory.WithFormFile(shelfId: "shelf-1", fileContent: csv, userId: "user-1"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var syncResult = Assert.IsType<BookImportResult>(ok.Value);
        Assert.Equal(2, syncResult.BooksFound);
        Assert.Equal(2, syncResult.BooksAdded);
        Assert.Equal(2, syncResult.Books.Count);
    }

    [Fact]
    public async Task UploadBookList_SkipsDuplicateTitlesCaseInsensitive()
    {
        var existingBook = new Book { Id = "b1", Title = "Dune" };
        var shelf = new Bookshelf { Id = "shelf-1", UserId = "user-1", Books = [existingBook] };

        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(shelf).Object);
        containerMock
            .Setup(c => c.ReplaceItemAsync(
                It.IsAny<Bookshelf>(), "shelf-1", It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Bookshelf s, string _, PartitionKey? _, ItemRequestOptions? _, CancellationToken _) =>
                CosmosFactory.ItemResponse(s).Object);
        var jobsMock = CosmosFactory.Container();
        jobsMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<BookCoverFetchJob>(), It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(new BookCoverFetchJob()).Object);

        var sut = CreateSut(CosmosFactory.CosmosDbService(
            bookshelvesContainer: containerMock, jobsContainer: jobsMock));

        // "DUNE" should be skipped (already exists as "Dune"), "Foundation" should be added
        var csv = "DUNE,Frank Herbert\nFoundation,Isaac Asimov";
        var result = await sut.UploadBookList(
            RequestFactory.WithFormFile(shelfId: "shelf-1", fileContent: csv),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var syncResult = Assert.IsType<BookImportResult>(ok.Value);
        Assert.Equal(2, syncResult.BooksFound);
        Assert.Equal(1, syncResult.BooksAdded); // only Foundation added
    }

    // ── ParseBookList – unit tests for CSV/pipe parsing ───────────────────────

    [Fact]
    public void ParseBookList_CsvFormat_ParsesTitleAndAuthor()
    {
        var entries = UploadFunctions.ParseBookList("Dune,Frank Herbert");

        Assert.Single(entries);
        Assert.Equal("Dune", entries[0].Title);
        Assert.Equal("Frank Herbert", entries[0].Author);
    }

    [Fact]
    public void ParseBookList_QuotedCsvFormat_ParsesTitleAndAuthor()
    {
        var entries = UploadFunctions.ParseBookList("\"Dune\",\"Frank Herbert\"");

        Assert.Single(entries);
        Assert.Equal("Dune", entries[0].Title);
        Assert.Equal("Frank Herbert", entries[0].Author);
    }

    [Fact]
    public void ParseBookList_PipeDelimitedFormat_ParsesTitleAndAuthor()
    {
        var entries = UploadFunctions.ParseBookList("\"Foundation\"|\"Isaac Asimov\"");

        Assert.Single(entries);
        Assert.Equal("Foundation", entries[0].Title);
        Assert.Equal("Isaac Asimov", entries[0].Author);
    }

    [Fact]
    public void ParseBookList_MissingAuthor_DefaultsToUnknown()
    {
        var entries = UploadFunctions.ParseBookList("Only A Title");

        Assert.Single(entries);
        Assert.Equal("Only A Title", entries[0].Title);
        Assert.Equal("Unknown", entries[0].Author);
    }

    [Fact]
    public void ParseBookList_MultipleLines_ParsesAll()
    {
        var text = "Dune,Frank Herbert\nFoundation,Isaac Asimov\nNeuromancer,William Gibson";
        var entries = UploadFunctions.ParseBookList(text);

        Assert.Equal(3, entries.Count);
        Assert.Equal("Dune", entries[0].Title);
        Assert.Equal("Foundation", entries[1].Title);
        Assert.Equal("Neuromancer", entries[2].Title);
    }

    [Fact]
    public void ParseBookList_EmptyLines_AreSkipped()
    {
        var text = "Dune,Frank Herbert\n\n\nFoundation,Isaac Asimov\n";
        var entries = UploadFunctions.ParseBookList(text);

        Assert.Equal(2, entries.Count);
    }

    [Fact]
    public void ParseBookList_EmptyInput_ReturnsEmptyList()
    {
        var entries = UploadFunctions.ParseBookList("");
        Assert.Empty(entries);
    }

    [Fact]
    public void ParseBookList_WhitespaceOnlyLines_AreSkipped()
    {
        var entries = UploadFunctions.ParseBookList("   \n   ");
        Assert.Empty(entries);
    }
}
