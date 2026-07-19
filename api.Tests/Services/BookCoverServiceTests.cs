using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using BookshelfWallpaper.Api.Tests.Helpers;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Net;

namespace BookshelfWallpaper.Api.Tests.Services;

public sealed class BookCoverServiceTests
{
    private static BookCoverService CreateSut(
        IHttpClientFactory http,
        Mock<IBlobStorageService>? blob = null)
    {
        if (blob is null)
        {
            blob = new Mock<IBlobStorageService>();
            blob.Setup(b => b.UploadFromUrlAsync(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync("https://storage.example.com/cover.jpg");
        }
        return new BookCoverService(http, blob.Object);
    }

    private static BookCoverFetchJob MakeJob(string? asin = null) =>
        new()
        {
            Id = "job-1",
            BookId = "book-1",
            ShelfId = "shelf-1",
            Title = "Dune",
            Author = "Frank Herbert",
            Asin = asin,
            Status = "pending",
        };

    // ── FindAndStoreCoverAsync success paths ──────────────────────────────────

    [Fact]
    public async Task FindAndStoreCoverAsync_WhenOpenLibraryAsinReturnsImage_ReturnsStoredUrl()
    {
        // HEAD /b/isbn/{asin}-L.jpg → 200 with image content-type
        var httpFactory = new HttpHandlerStub(
            req =>
            {
                var resp = new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK);
                resp.Content = new System.Net.Http.ByteArrayContent([]);
                resp.Content.Headers.ContentType =
                    new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
                return resp;
            }).ToFactory();

        var blobMock = new Mock<IBlobStorageService>();
        blobMock.Setup(b => b.UploadFromUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.com/cover.jpg");

        var sut = CreateSut(httpFactory, blobMock);

        var result = await sut.FindAndStoreCoverAsync(MakeJob(asin: "B001234"), NullLogger.Instance);

        Assert.NotNull(result);
        Assert.Equal("https://storage.example.com/cover.jpg", result);
        blobMock.Verify(b => b.UploadFromUrlAsync(
            "book-covers", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task FindAndStoreCoverAsync_WhenOpenLibrarySearchHasCoverId_ReturnsStoredUrl()
    {
        // ASIN HEAD → not image; OpenLibrary search → JSON with cover_i
        var callCount = 0;
        var httpFactory = new HttpHandlerStub(req =>
        {
            callCount++;
            // First call (HEAD for ASIN) returns non-image
            if (req.Method == System.Net.Http.HttpMethod.Head)
                return new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK);
            // Second call (OpenLibrary search GET) returns JSON with cover_i
            var olJson = """{"numFound":1,"docs":[{"cover_i":99999}]}""";
            return new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(olJson, System.Text.Encoding.UTF8, "application/json"),
            };
        }).ToFactory();

        var blobMock = new Mock<IBlobStorageService>();
        blobMock.Setup(b => b.UploadFromUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.Is<string>(u => u.Contains("covers.openlibrary.org/b/id/99999")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.com/ol-cover.jpg");

        var sut = CreateSut(httpFactory, blobMock);

        var result = await sut.FindAndStoreCoverAsync(MakeJob(asin: "some-asin"), NullLogger.Instance);

        Assert.NotNull(result);
        Assert.Equal("https://storage.example.com/ol-cover.jpg", result);
    }

    [Fact]
    public async Task FindAndStoreCoverAsync_WhenGoogleBooksThumbnailFound_ReturnsStoredUrl()
    {
        // All earlier sources fail; Google Books returns a thumbnail
        var httpFactory = new HttpHandlerStub(req =>
        {
            if (req.RequestUri!.Host.Contains("openlibrary"))
                return new System.Net.Http.HttpResponseMessage(HttpStatusCode.NotFound);
            // Google Books response
            var json = """
            {
                "totalItems": 1,
                "items": [{
                    "volumeInfo": {
                        "imageLinks": {
                            "thumbnail": "https://books.google.com/cover?zoom=1"
                        }
                    }
                }]
            }
            """;
            return new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        }).ToFactory();

        var blobMock = new Mock<IBlobStorageService>();
        blobMock.Setup(b => b.UploadFromUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(),
                It.Is<string>(u => u.Contains("books.google.com")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.com/google-cover.jpg");

        var sut = CreateSut(httpFactory, blobMock);
        var result = await sut.FindAndStoreCoverAsync(MakeJob(), NullLogger.Instance);

        Assert.NotNull(result);
    }

    [Fact]
    public async Task FindAndStoreCoverAsync_WhenAllSourcesFail_ReturnsNull()
    {
        var httpFactory = new HttpHandlerStub(
            _ => new System.Net.Http.HttpResponseMessage(HttpStatusCode.NotFound)).ToFactory();

        var sut = CreateSut(httpFactory);

        var result = await sut.FindAndStoreCoverAsync(MakeJob(), NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAndStoreCoverAsync_WhenBlobUploadThrows_ReturnsNull()
    {
        // Image found but blob upload fails
        var httpFactory = new HttpHandlerStub(req =>
        {
            var resp = new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK);
            resp.Content = new System.Net.Http.ByteArrayContent([]);
            resp.Content.Headers.ContentType =
                new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");
            return resp;
        }).ToFactory();

        var blobMock = new Mock<IBlobStorageService>();
        blobMock.Setup(b => b.UploadFromUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("Storage error"));

        var sut = CreateSut(httpFactory, blobMock);

        var result = await sut.FindAndStoreCoverAsync(MakeJob(asin: "B001"), NullLogger.Instance);

        Assert.Null(result);
    }

    [Fact]
    public async Task FindAndStoreCoverAsync_WhenNoAsin_SkipsAsinLookup()
    {
        var headCalled = false;
        var httpFactory = new HttpHandlerStub(req =>
        {
            if (req.Method == System.Net.Http.HttpMethod.Head)
                headCalled = true;
            return new System.Net.Http.HttpResponseMessage(HttpStatusCode.NotFound);
        }).ToFactory();

        var sut = CreateSut(httpFactory);
        await sut.FindAndStoreCoverAsync(MakeJob(asin: null), NullLogger.Instance);

        Assert.False(headCalled, "HEAD request should not be made when no ASIN is available");
    }

    [Fact]
    public async Task FindAndStoreCoverAsync_GoogleThumbnailUpgradedToZoom2()
    {
        var capturedSourceUrl = string.Empty;
        var httpFactory = new HttpHandlerStub(req =>
        {
            if (req.RequestUri!.Host.Contains("openlibrary"))
                return new System.Net.Http.HttpResponseMessage(HttpStatusCode.NotFound);
            var json = """
            {
                "totalItems": 1,
                "items": [{"volumeInfo": {"imageLinks": {"thumbnail": "http://books.google.com/cover?zoom=1"}}}]
            }
            """;
            return new System.Net.Http.HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json"),
            };
        }).ToFactory();

        var blobMock = new Mock<IBlobStorageService>();
        blobMock
            .Setup(b => b.UploadFromUrlAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Callback<string, string, string, CancellationToken>((_, _, url, _) => capturedSourceUrl = url)
            .ReturnsAsync("https://storage.example.com/cover.jpg");

        await CreateSut(httpFactory, blobMock).FindAndStoreCoverAsync(MakeJob(), NullLogger.Instance);

        // Thumbnail should have been upgraded to zoom=2 and HTTP→HTTPS
        Assert.Contains("zoom=2", capturedSourceUrl);
        Assert.StartsWith("https://", capturedSourceUrl);
    }
}
