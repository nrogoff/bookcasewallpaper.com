using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using BookshelfWallpaper.Api.Tests.Helpers;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net;
using System.Text.Json;

namespace BookshelfWallpaper.Api.Tests.Services;

public sealed class AudibleServiceTests
{
    private static AudibleService CreateSut(
        Mock<ICosmosDbService>? cosmos = null,
        IHttpClientFactory? http = null) =>
        new(cosmos?.Object ?? CosmosFactory.CosmosDbService().Object,
            http ?? HttpHandlerStub.Silent());

    private static AudibleConnection ValidConnection(string userId = "user-1") => new()
    {
        Id = userId,
        UserId = userId,
        AccessToken = "valid-access-token",
        RefreshToken = "valid-refresh-token",
        TokenType = "Bearer",
        Marketplace = "UK",
        ExpiresAt = DateTime.UtcNow.AddHours(1).ToString("O"),
    };

    private static string MakeAudibleLibraryJson(params (string Asin, string Title, string Author)[] items)
    {
        var docs = items.Select(i => new
        {
            asin = i.Asin,
            title = i.Title,
            authors = new[] { new { name = i.Author } },
        });
        return JsonSerializer.Serialize(new { items = docs });
    }

    // ── HasValidAudibleConnectionAsync ────────────────────────────────────────

    [Fact]
    public async Task HasValidAudibleConnectionAsync_WhenNoConnection_ReturnsFalse()
    {
        var connContainer = CosmosFactory.Container();
        connContainer
            .Setup(c => c.ReadItemAsync<AudibleConnection>("user-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosFactory.NotFound());

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
            audibleConnectionsContainer: connContainer));

        Assert.False(await sut.HasValidAudibleConnectionAsync("user-1"));
    }

    [Fact]
    public async Task HasValidAudibleConnectionAsync_WhenTokenValid_ReturnsTrue()
    {
        var connContainer = CosmosFactory.Container();
        connContainer
            .Setup(c => c.ReadItemAsync<AudibleConnection>("user-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(ValidConnection()).Object);

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
            audibleConnectionsContainer: connContainer));

        Assert.True(await sut.HasValidAudibleConnectionAsync("user-1"));
    }

    [Fact]
    public async Task HasValidAudibleConnectionAsync_WhenTokenExpired_ReturnsFalse()
    {
        var expired = ValidConnection();
        expired.ExpiresAt = DateTime.UtcNow.AddHours(-1).ToString("O");

        var connContainer = CosmosFactory.Container();
        connContainer
            .Setup(c => c.ReadItemAsync<AudibleConnection>("user-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(expired).Object);

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
            audibleConnectionsContainer: connContainer));

        Assert.False(await sut.HasValidAudibleConnectionAsync("user-1"));
    }

    // ── SyncAudibleIntoShelfAsync – validation ────────────────────────────────

    [Fact]
    public async Task SyncAudibleIntoShelfAsync_WhenShelfNotFound_Throws404()
    {
        var shelvesContainer = CosmosFactory.Container();
        shelvesContainer
            .Setup(c => c.ReadItemAsync<Bookshelf>(It.IsAny<string>(),
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosFactory.NotFound());

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
            bookshelvesContainer: shelvesContainer));

        var ex = await Assert.ThrowsAsync<AudibleSyncException>(
            () => sut.SyncAudibleIntoShelfAsync("user-1", "missing-shelf", null, NullLogger.Instance));

        Assert.Equal(404, ex.Status);
    }

    [Fact]
    public async Task SyncAudibleIntoShelfAsync_WhenNoAccessToken_Throws428()
    {
        Environment.SetEnvironmentVariable("AUDIBLE_ACCESS_TOKEN", null);
        try
        {
            var shelf = new Bookshelf { Id = "shelf-1", UserId = "user-1" };
            var shelvesContainer = CosmosFactory.Container();
            shelvesContainer
                .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1",
                    It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
                .ReturnsAsync(CosmosFactory.ItemResponse(shelf).Object);

            var connContainer = CosmosFactory.Container();
            connContainer
                .Setup(c => c.ReadItemAsync<AudibleConnection>(It.IsAny<string>(),
                    It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
                .ThrowsAsync(CosmosFactory.NotFound());

            var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
                bookshelvesContainer: shelvesContainer,
                audibleConnectionsContainer: connContainer));

            var ex = await Assert.ThrowsAsync<AudibleSyncException>(
                () => sut.SyncAudibleIntoShelfAsync("user-1", "shelf-1", null, NullLogger.Instance));

            Assert.Equal(428, ex.Status);
        }
        finally { Environment.SetEnvironmentVariable("AUDIBLE_ACCESS_TOKEN", null); }
    }

    [Fact]
    public async Task SyncAudibleIntoShelfAsync_WithValidToken_SkipsExistingAsins()
    {
        var existingBook = new Book { Id = "b1", Title = "Dune", Asin = "B001" };
        var shelf = new Bookshelf { Id = "shelf-1", UserId = "user-1", Books = [existingBook] };

        var shelvesContainer = CosmosFactory.Container();
        shelvesContainer
            .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(shelf).Object);
        shelvesContainer
            .Setup(c => c.ReplaceItemAsync(
                It.IsAny<Bookshelf>(), "shelf-1", It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Bookshelf s, string _, PartitionKey? _, ItemRequestOptions? _, CancellationToken _) =>
                CosmosFactory.ItemResponse(s).Object);

        var connContainer = CosmosFactory.Container();
        connContainer
            .Setup(c => c.ReadItemAsync<AudibleConnection>("user-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(ValidConnection()).Object);

        var jobsContainer = CosmosFactory.Container();
        jobsContainer
            .Setup(c => c.CreateItemAsync(
                It.IsAny<BookCoverFetchJob>(), It.IsAny<PartitionKey?>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(new BookCoverFetchJob()).Object);

        var cosmos = CosmosFactory.CosmosDbService(
            bookshelvesContainer: shelvesContainer,
            jobsContainer: jobsContainer,
            audibleConnectionsContainer: connContainer);

        // Audible API returns: B001 (already exists) + B002 (new)
        var libraryJson = MakeAudibleLibraryJson(("B001", "Dune", "Herbert"), ("B002", "Foundation", "Asimov"));
        var http = new HttpHandlerStub(libraryJson);

        var sut = CreateSut(cosmos: cosmos, http: http.ToFactory());

        var result = await sut.SyncAudibleIntoShelfAsync("user-1", "shelf-1", "UK", NullLogger.Instance);

        Assert.Equal(2, result.BooksFound);
        Assert.Equal(1, result.BooksAdded); // B001 skipped, only B002 added
        Assert.Equal("Foundation", result.Books[0].Title);
    }

    [Fact]
    public async Task SyncAudibleIntoShelfAsync_WhenAudibleReturns403_Throws403()
    {
        var shelf = new Bookshelf { Id = "shelf-1", UserId = "user-1" };

        var shelvesContainer = CosmosFactory.Container();
        shelvesContainer
            .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(shelf).Object);

        var connContainer = CosmosFactory.Container();
        connContainer
            .Setup(c => c.ReadItemAsync<AudibleConnection>("user-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(ValidConnection()).Object);

        var cosmos = CosmosFactory.CosmosDbService(
            bookshelvesContainer: shelvesContainer,
            audibleConnectionsContainer: connContainer);

        // All HTTP requests return 403
        var http = new HttpHandlerStub(
            _ => new System.Net.Http.HttpResponseMessage(HttpStatusCode.Forbidden));

        var sut = CreateSut(cosmos: cosmos, http: http.ToFactory());

        var ex = await Assert.ThrowsAsync<AudibleSyncException>(
            () => sut.SyncAudibleIntoShelfAsync("user-1", "shelf-1", "UK", NullLogger.Instance));

        Assert.Equal(403, ex.Status);
    }

    // ── GetLatestShelfIdForUserAsync ──────────────────────────────────────────

    [Fact]
    public async Task GetLatestShelfIdForUserAsync_WhenShelvesExist_ReturnsFirstId()
    {
        dynamic row = new System.Dynamic.ExpandoObject();
        row.id = "shelf-42";

        var shelvesContainer = CosmosFactory.Container();
        shelvesContainer
            .Setup(c => c.GetItemQueryIterator<dynamic>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(CosmosFactory.SinglePageIterator<dynamic>(
                [(object)row]).Object);

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
            bookshelvesContainer: shelvesContainer));

        var id = await sut.GetLatestShelfIdForUserAsync("user-1");

        Assert.Equal("shelf-42", id);
    }

    [Fact]
    public async Task GetLatestShelfIdForUserAsync_WhenNoShelves_ReturnsNull()
    {
        var shelvesContainer = CosmosFactory.Container();
        shelvesContainer
            .Setup(c => c.GetItemQueryIterator<dynamic>(
                It.IsAny<QueryDefinition>(), It.IsAny<string?>(), It.IsAny<QueryRequestOptions?>()))
            .Returns(CosmosFactory.EmptyIterator<dynamic>().Object);

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(
            bookshelvesContainer: shelvesContainer));

        var id = await sut.GetLatestShelfIdForUserAsync("user-1");

        Assert.Null(id);
    }
}
