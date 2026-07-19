using BookshelfWallpaper.Api.Functions;
using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using BookshelfWallpaper.Api.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace BookshelfWallpaper.Api.Tests.Functions;

public sealed class BookshelfFunctionsTests
{
    private static BookshelfFunctions CreateSut(Mock<ICosmosDbService>? cosmos = null) =>
        new(cosmos?.Object ?? CosmosFactory.CosmosDbService().Object,
            NullLogger<BookshelfFunctions>.Instance);

    // ── GetBookshelves ────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBookshelves_WithUserHeader_ReturnsOkWithMatchingShelves()
    {
        var shelf = new Bookshelf { Id = "s1", UserId = "user-1", Name = "Shelf A" };
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.GetItemQueryIterator<Bookshelf>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(CosmosFactory.SinglePageIterator([shelf]).Object);

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));
        var req = RequestFactory.Create(userId: "user-1");

        var result = await sut.GetBookshelves(req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var list = Assert.IsType<List<Bookshelf>>(ok.Value);
        Assert.Single(list);
        Assert.Equal("Shelf A", list[0].Name);
    }

    [Fact]
    public async Task GetBookshelves_WithoutHeader_ReturnsOkWithEmptyList()
    {
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.GetItemQueryIterator<Bookshelf>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(CosmosFactory.EmptyIterator<Bookshelf>().Object);

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));
        var req = RequestFactory.Create(); // no userId header

        var result = await sut.GetBookshelves(req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<List<Bookshelf>>(ok.Value));
    }

    [Fact]
    public async Task GetBookshelves_WhenCosmosThrows_Returns500()
    {
        var cosmos = new Mock<ICosmosDbService>();
        cosmos.Setup(c => c.GetBookshelvesContainerAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Cosmos unavailable"));
        var sut = CreateSut(cosmos);

        var result = await sut.GetBookshelves(RequestFactory.Create(), CancellationToken.None);

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ── GetBookshelf ──────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBookshelf_WithValidId_ReturnsOkWithShelf()
    {
        var shelf = new Bookshelf { Id = "shelf-1", UserId = "user-1", Name = "My Shelf" };
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(shelf).Object);

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));

        var result = await sut.GetBookshelf(
            RequestFactory.Create(userId: "user-1"), "shelf-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("My Shelf", Assert.IsType<Bookshelf>(ok.Value).Name);
    }

    [Fact]
    public async Task GetBookshelf_WhenNotFound_Returns404()
    {
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosFactory.NotFound());

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));

        var result = await sut.GetBookshelf(RequestFactory.Create(), "missing-id", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GetBookshelf_WhenCosmosThrows_Returns500()
    {
        var cosmos = new Mock<ICosmosDbService>();
        cosmos.Setup(c => c.GetBookshelvesContainerAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));
        var sut = CreateSut(cosmos);

        var result = await sut.GetBookshelf(RequestFactory.Create(), "id", CancellationToken.None);

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ── CreateBookshelf ───────────────────────────────────────────────────────

    [Fact]
    public async Task CreateBookshelf_WithValidName_Returns201WithNewShelf()
    {
        Bookshelf? captured = null;
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.CreateItemAsync(
                It.IsAny<Bookshelf>(), It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .Callback<Bookshelf, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, _, _, _) => captured = item)
            .Returns(() => Task.FromResult(CosmosFactory.ItemResponse(captured!).Object));

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));
        var req = RequestFactory.Create(userId: "user-1", body: new { name = "New Shelf" }, method: "POST");

        var result = await sut.CreateBookshelf(req, CancellationToken.None);

        var obj = Assert.IsType<ObjectResult>(result);
        Assert.Equal(201, obj.StatusCode);
        var shelf = Assert.IsType<Bookshelf>(obj.Value);
        Assert.Equal("New Shelf", shelf.Name);
        Assert.Equal("user-1", shelf.UserId);
    }

    [Fact]
    public async Task CreateBookshelf_WithEmptyName_ReturnsBadRequest()
    {
        var result = await CreateSut().CreateBookshelf(
            RequestFactory.Create(body: new { name = "  " }, method: "POST"),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task CreateBookshelf_WithNullBody_ReturnsBadRequest()
    {
        var result = await CreateSut().CreateBookshelf(
            RequestFactory.Create(method: "POST"), // no body
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    // ── UpdateBookshelf ───────────────────────────────────────────────────────

    [Fact]
    public async Task UpdateBookshelf_WhenFound_UpdatesNameAndReturnsOk()
    {
        var existing = new Bookshelf { Id = "shelf-1", UserId = "user-1", Name = "Old Name" };
        Bookshelf? replaced = null;

        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(existing).Object);
        containerMock
            .Setup(c => c.ReplaceItemAsync(
                It.IsAny<Bookshelf>(), "shelf-1", It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .Callback<Bookshelf, string, PartitionKey?, ItemRequestOptions?, CancellationToken>(
                (item, _, _, _, _) => replaced = item)
            .Returns(() => Task.FromResult(CosmosFactory.ItemResponse(replaced!).Object));

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));
        var req = RequestFactory.Create(userId: "user-1", body: new { name = "New Name" }, method: "PATCH");

        var result = await sut.UpdateBookshelf(req, "shelf-1", CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Equal("New Name", Assert.IsType<Bookshelf>(ok.Value).Name);
    }

    [Fact]
    public async Task UpdateBookshelf_WhenNotFound_Returns404()
    {
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosFactory.NotFound());

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));

        var result = await sut.UpdateBookshelf(
            RequestFactory.Create(body: new { name = "X" }, method: "PATCH"),
            "missing-id", CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    // ── DeleteBookshelf ───────────────────────────────────────────────────────

    [Fact]
    public async Task DeleteBookshelf_WhenExists_Returns204()
    {
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.DeleteItemAsync<Bookshelf>(
                "shelf-1", It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(new Bookshelf()).Object);

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));

        var result = await sut.DeleteBookshelf(
            RequestFactory.Create(userId: "user-1"), "shelf-1", CancellationToken.None);

        Assert.Equal(204, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Fact]
    public async Task DeleteBookshelf_WhenAlreadyGone_Returns204_Idempotent()
    {
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.DeleteItemAsync<Bookshelf>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosFactory.NotFound());

        var sut = CreateSut(CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));

        var result = await sut.DeleteBookshelf(RequestFactory.Create(), "gone-id", CancellationToken.None);

        Assert.Equal(204, Assert.IsType<StatusCodeResult>(result).StatusCode);
    }

    [Fact]
    public async Task DeleteBookshelf_WhenCosmosThrows_Returns500()
    {
        var cosmos = new Mock<ICosmosDbService>();
        cosmos.Setup(c => c.GetBookshelvesContainerAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));
        var sut = CreateSut(cosmos);

        var result = await sut.DeleteBookshelf(RequestFactory.Create(), "id", CancellationToken.None);

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
    }
}
