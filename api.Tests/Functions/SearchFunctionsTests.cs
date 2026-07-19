using BookshelfWallpaper.Api.Functions;
using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using System.Net;

namespace BookshelfWallpaper.Api.Tests.Functions;

public sealed class SearchFunctionsTests
{
    private static SearchFunctions CreateSut(IHttpClientFactory httpFactory) =>
        new(httpFactory, NullLogger<SearchFunctions>.Instance);

    private static string BuildOpenLibraryResponse(string title, string author, long? coverId = null)
    {
        var coverPart = coverId.HasValue ? $", \"cover_i\": {coverId}" : "";
        return $$"""
        {
            "numFound": 1,
            "docs": [
                {
                    "title": "{{title}}",
                    "author_name": ["{{author}}"]
                    {{coverPart}}
                }
            ]
        }
        """;
    }

    // ── SearchBooks ───────────────────────────────────────────────────────────

    [Fact]
    public async Task SearchBooks_WithValidQuery_ReturnsMatchingResults()
    {
        var json = BuildOpenLibraryResponse("Dune", "Frank Herbert", 12345);
        var sut = CreateSut(new HttpHandlerStub(json).ToFactory());
        var req = RequestFactory.Create(query: new Dictionary<string, string> { ["q"] = "Dune" });

        var result = await sut.SearchBooks(req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var books = Assert.IsType<List<BookSearchResult>>(ok.Value);
        Assert.Single(books);
        Assert.Equal("Dune", books[0].Title);
        Assert.Equal("Frank Herbert", books[0].Author);
        Assert.Contains("covers.openlibrary.org", books[0].CoverUrl);
    }

    [Fact]
    public async Task SearchBooks_WhenNoCoverIdInResponse_SetsCoverUrlToNull()
    {
        var json = BuildOpenLibraryResponse("Dune", "Frank Herbert"); // no cover_i
        var sut = CreateSut(new HttpHandlerStub(json).ToFactory());
        var req = RequestFactory.Create(query: new Dictionary<string, string> { ["q"] = "Dune" });

        var result = await sut.SearchBooks(req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var books = Assert.IsType<List<BookSearchResult>>(ok.Value);
        Assert.Single(books);
        Assert.Null(books[0].CoverUrl);
    }

    [Fact]
    public async Task SearchBooks_WithEmptyDocs_ReturnsEmptyList()
    {
        var json = """{ "numFound": 0, "docs": [] }""";
        var sut = CreateSut(new HttpHandlerStub(json).ToFactory());
        var req = RequestFactory.Create(query: new Dictionary<string, string> { ["q"] = "zzznotabook" });

        var result = await sut.SearchBooks(req, CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        Assert.Empty(Assert.IsType<List<BookSearchResult>>(ok.Value));
    }

    [Fact]
    public async Task SearchBooks_WithSingleCharQuery_ReturnsBadRequest()
    {
        var sut = CreateSut(HttpHandlerStub.Silent());
        var req = RequestFactory.Create(query: new Dictionary<string, string> { ["q"] = "X" });

        var result = await sut.SearchBooks(req, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SearchBooks_WithEmptyQuery_ReturnsBadRequest()
    {
        var sut = CreateSut(HttpHandlerStub.Silent());
        var req = RequestFactory.Create(query: new Dictionary<string, string> { ["q"] = "" });

        var result = await sut.SearchBooks(req, CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task SearchBooks_WhenHttpFails_Returns500()
    {
        var sut = CreateSut(new HttpHandlerStub(
            req => new System.Net.Http.HttpResponseMessage(HttpStatusCode.InternalServerError)).ToFactory());

        var req = RequestFactory.Create(query: new Dictionary<string, string> { ["q"] = "Dune" });

        var result = await sut.SearchBooks(req, CancellationToken.None);

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    [Fact]
    public async Task SearchBooks_ReturnsSourceAsOpenlibrary()
    {
        var json = BuildOpenLibraryResponse("Dune", "Frank Herbert");
        var sut = CreateSut(new HttpHandlerStub(json).ToFactory());
        var req = RequestFactory.Create(query: new Dictionary<string, string> { ["q"] = "Dune" });

        var result = await sut.SearchBooks(req, CancellationToken.None);

        var books = Assert.IsType<List<BookSearchResult>>(
            Assert.IsType<OkObjectResult>(result).Value);
        Assert.All(books, b => Assert.Equal("openlibrary", b.Source));
    }
}
