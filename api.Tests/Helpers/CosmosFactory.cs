using Microsoft.Azure.Cosmos;
using BookshelfWallpaper.Api.Services;
using Moq;
using System.Net;

namespace BookshelfWallpaper.Api.Tests.Helpers;

/// <summary>Factory helpers for building Cosmos SDK mocks.</summary>
internal static class CosmosFactory
{
    internal static Mock<ItemResponse<T>> ItemResponse<T>(T resource)
    {
        var mock = new Mock<ItemResponse<T>>();
        mock.Setup(r => r.Resource).Returns(resource);
        return mock;
    }

    internal static Mock<FeedIterator<T>> EmptyIterator<T>()
    {
        var mock = new Mock<FeedIterator<T>>();
        mock.Setup(i => i.HasMoreResults).Returns(false);
        return mock;
    }

    internal static Mock<FeedIterator<T>> SinglePageIterator<T>(IEnumerable<T> items)
    {
        var list = items.ToList();
        var responseMock = new Mock<FeedResponse<T>>();
        responseMock.Setup(r => r.GetEnumerator()).Returns(() => list.GetEnumerator());

        var iterMock = new Mock<FeedIterator<T>>();
        iterMock.SetupSequence(i => i.HasMoreResults).Returns(true).Returns(false);
        iterMock.Setup(i => i.ReadNextAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(responseMock.Object);
        return iterMock;
    }

    internal static CosmosException NotFound() =>
        new("Not Found", HttpStatusCode.NotFound, 0, "test-activity-id", 0);

    internal static Mock<Container> Container() => new();

    /// <summary>
    /// Creates a fully-wired <see cref="ICosmosDbService"/> mock.
    /// Pass pre-configured container mocks or leave null for pass-through mocks.
    /// </summary>
    internal static Mock<ICosmosDbService> CosmosDbService(
        Mock<Container>? bookshelvesContainer = null,
        Mock<Container>? jobsContainer = null,
        Mock<Container>? audibleConnectionsContainer = null)
    {
        var mock = new Mock<ICosmosDbService>();
        mock.Setup(c => c.GetBookshelvesContainerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((bookshelvesContainer ?? Container()).Object);
        mock.Setup(c => c.GetJobsContainerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((jobsContainer ?? Container()).Object);
        mock.Setup(c => c.GetAudibleConnectionsContainerAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync((audibleConnectionsContainer ?? Container()).Object);
        return mock;
    }
}
