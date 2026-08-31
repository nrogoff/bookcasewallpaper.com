using BookshelfWallpaper.Api.Functions;
using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using BookshelfWallpaper.Api.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BookshelfWallpaper.Api.Tests.Functions;

public sealed class BookFunctionsTests
{
    private static BookFunctions CreateSut(Mock<ICosmosDbService>? cosmos = null) =>
        new(cosmos?.Object ?? CosmosFactory.CosmosDbService().Object,
            NullLogger<BookFunctions>.Instance);

    private static Bookshelf MakeShelf(string id = "shelf-1", string userId = "user-1",
        List<Book>? books = null) =>
        new() { Id = id, UserId = userId, Name = "Test Shelf", Books = books ?? [] };

    // ── AddBook ───────────────────────────────────────────────────────────────

    [Fact]
    public async Task AddBook_WithValidTitle_ReturnsOkWithUpdatedShelf()
    {
        var shelf = MakeShelf();
        Bookshelf? replaced = null;

        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(shelf).Object);
        containerMock
            .Setup(c => c.ReplaceItemAsync(
                It.IsAny<Bookshelf>(), "shelf-1", It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .Callback<Bookshelf, string, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, _, _, _, _) => replaced = item)
            .Returns(() => Task.FromResult(CosmosFactory.ItemResponse(replaced!).Object));

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));
        var req = RequestFactory.Create(
            userId: "user-1",
            body: new { title = "Dune", author = "Frank Herbert" },
            method: "POST");

        var result = await sut.AddBook(req, "shelf-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var updated = Assert.IsType<Bookshelf>(ok.Value);
        Assert.Single(updated.Books);
        Assert.Equal("Dune", updated.Books[0].Title);
        Assert.Equal("Frank Herbert", updated.Books[0].Author);
    }

    [Fact]
    public async Task AddBook_WithNoCoverUrl_QueuesCoverFetchJob()
    {
        var shelf = MakeShelf();
        var jobsMock = CosmosFactory.Container();
        jobsMock.Setup(c => c.CreateItemAsync(
                It.IsAny<BookCoverFetchJob>(), It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(new BookCoverFetchJob()).Object);

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

        var sut = CreateSut(CosmosFactory.CosmosDbService(
            bookshelvesContainer: containerMock, jobsContainer: jobsMock));

        var req = RequestFactory.Create(
            userId: "user-1",
            body: new { title = "Dune" }, // no coverUrl
            method: "POST");

        await sut.AddBook(req, "shelf-1", CancellationToken.None);

        jobsMock.Verify(c => c.CreateItemAsync(
            It.IsAny<BookCoverFetchJob>(), It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task AddBook_WithCoverUrl_DoesNotQueueCoverFetchJob()
    {
        var shelf = MakeShelf();
        var jobsMock = CosmosFactory.Container();

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

        var sut = CreateSut(CosmosFactory.CosmosDbService(
            bookshelvesContainer: containerMock, jobsContainer: jobsMock));

        var req = RequestFactory.Create(
            userId: "user-1",
            body: new { title = "Dune", coverUrl = "https://example.com/cover.jpg" },
            method: "POST");

        await sut.AddBook(req, "shelf-1", CancellationToken.None);

        jobsMock.Verify(c => c.CreateItemAsync(
            It.IsAny<BookCoverFetchJob>(), It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [Fact]
    public async Task AddBook_WithMissingTitle_ReturnsBadRequest()
    {
        var result = await CreateSut().AddBook(
            RequestFactory.Create(body: new { title = "" }, method: "POST"),
            "shelf-1", CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task AddBook_WhenShelfNotFound_Returns404()
    {
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosFactory.NotFound());

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));

        var result = await sut.AddBook(
            RequestFactory.Create(body: new { title = "Dune" }, method: "POST"),
            "missing-shelf", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task AddBook_WhenCosmosThrows_Returns500()
    {
        var cosmos = new Mock<ICosmosDbService>();
        cosmos.Setup(c => c.GetBookshelvesContainerAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));
        var sut = CreateSut(cosmos);

        var result = await sut.AddBook(
            RequestFactory.Create(body: new { title = "X" }, method: "POST"),
            "shelf-1", CancellationToken.None);

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ── RemoveBook ────────────────────────────────────────────────────────────

    [Fact]
    public async Task RemoveBook_WhenBookOnShelf_ReturnsOkWithBookRemoved()
    {
        var book = new Book { Id = "book-1", Title = "Dune" };
        var shelf = MakeShelf(books: [book]);
        Bookshelf? replaced = null;

        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(shelf).Object);
        containerMock
            .Setup(c => c.ReplaceItemAsync(
                It.IsAny<Bookshelf>(), "shelf-1", It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .Callback<Bookshelf, string, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, _, _, _, _) => replaced = item)
            .Returns(() => Task.FromResult(CosmosFactory.ItemResponse(replaced!).Object));

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));

        var result = await sut.RemoveBook(
            RequestFactory.Create(userId: "user-1"), "shelf-1", "book-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<Bookshelf>(ok.Value).Books);
    }

    [Fact]
    public async Task RemoveBook_WhenBookNotOnShelf_Returns404()
    {
        var shelf = MakeShelf();

        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(shelf).Object);

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));

        var result = await sut.RemoveBook(
            RequestFactory.Create(), "shelf-1", "nonexistent-book", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task RemoveBook_WhenShelfNotFound_Returns404()
    {
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosFactory.NotFound());

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));

        var result = await sut.RemoveBook(
            RequestFactory.Create(), "missing-shelf", "book-1", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task RemoveBook_WhenCosmosThrows_Returns500()
    {
        var cosmos = new Mock<ICosmosDbService>();
        cosmos.Setup(c => c.GetBookshelvesContainerAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));
        var sut = CreateSut(cosmos);

        var result = await sut.RemoveBook(
            RequestFactory.Create(), "shelf-1", "book-1", CancellationToken.None);

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
    }
}
