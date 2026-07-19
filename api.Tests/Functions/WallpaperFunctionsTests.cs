using BookshelfWallpaper.Api.Functions;
using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using BookshelfWallpaper.Api.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using SkiaSharp;

namespace BookshelfWallpaper.Api.Tests.Functions;

public sealed class WallpaperFunctionsTests
{
    private static WallpaperFunctions CreateSut(
        Mock<ICosmosDbService>? cosmos = null,
        Mock<IBlobStorageService>? blob = null,
        IHttpClientFactory? http = null)
    {
        blob ??= new Mock<IBlobStorageService>();
        blob.Setup(b => b.UploadBufferAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.com/wallpaper.png");

        return new WallpaperFunctions(
            cosmos?.Object ?? CosmosFactory.CosmosDbService().Object,
            blob.Object,
            http ?? HttpHandlerStub.Silent(),
            NullLogger<WallpaperFunctions>.Instance);
    }

    // ── GenerateWallpaper – boundary tests ────────────────────────────────────

    [Fact]
    public async Task GenerateWallpaper_WithMissingShelfId_ReturnsBadRequest()
    {
        var result = await CreateSut().GenerateWallpaper(
            RequestFactory.Create(body: new { shelfId = "" }, method: "POST"),
            CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GenerateWallpaper_WithNullBody_ReturnsBadRequest()
    {
        var result = await CreateSut().GenerateWallpaper(
            RequestFactory.Create(method: "POST"), CancellationToken.None);

        Assert.IsType<BadRequestObjectResult>(result);
    }

    [Fact]
    public async Task GenerateWallpaper_WhenShelfNotFound_Returns404()
    {
        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>(
                It.IsAny<string>(), It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ThrowsAsync(CosmosFactory.NotFound());

        var sut = CreateSut(cosmos: CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock));

        var result = await sut.GenerateWallpaper(
            RequestFactory.Create(body: new { shelfId = "missing" }, method: "POST"),
            CancellationToken.None);

        Assert.IsType<NotFoundObjectResult>(result);
    }

    [Fact]
    public async Task GenerateWallpaper_WithValidShelfAndNoCoverImages_ReturnsOkWithUrls()
    {
        // Books with no imageUrl → SkiaSharp DrawFallbackSpine only (no HTTP calls)
        var shelf = new Bookshelf
        {
            Id = "shelf-1",
            UserId = "user-1",
            Name = "My Shelf",
            Settings = new BookshelfSettings
            {
                Width = 800,
                Height = 600,
                ShelfCount = 2,
                BooksPerShelf = 5,
                WallColor = "#2d2d2d",
                ShelfColor = "#8B4513",
                ShowTitles = true,
            },
            Books =
            [
                new Book { Id = "b1", Title = "Dune", Author = "Herbert" },
                new Book { Id = "b2", Title = "Foundation", Author = "Asimov" },
            ],
        };

        var containerMock = CosmosFactory.Container();
        containerMock
            .Setup(c => c.ReadItemAsync<Bookshelf>("shelf-1",
                It.IsAny<PartitionKey>(), null, It.IsAny<CancellationToken>()))
            .ReturnsAsync(CosmosFactory.ItemResponse(shelf).Object);

        var blobMock = new Mock<IBlobStorageService>();
        blobMock.Setup(b => b.UploadBufferAsync(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync("https://storage.example.com/wallpaper.png");

        var sut = CreateSut(
            cosmos: CosmosFactory.CosmosDbService(bookshelvesContainer: containerMock),
            blob: blobMock);

        var result = await sut.GenerateWallpaper(
            RequestFactory.Create(userId: "user-1", body: new { shelfId = "shelf-1" }, method: "POST"),
            CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var wallpaper = Assert.IsType<WallpaperGenerationResult>(ok.Value);
        Assert.NotNull(wallpaper.ImageUrl);
        Assert.NotNull(wallpaper.ThumbnailUrl);
        // Blob upload called twice (full image + thumbnail)
        blobMock.Verify(b => b.UploadBufferAsync(
                "wallpapers", It.IsAny<string>(), It.IsAny<byte[]>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [Fact]
    public async Task GenerateWallpaper_WhenCosmosThrows_Returns500()
    {
        var cosmos = new Mock<ICosmosDbService>();
        cosmos.Setup(c => c.GetBookshelvesContainerAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new Exception("DB error"));
        var sut = CreateSut(cosmos: cosmos);

        var result = await sut.GenerateWallpaper(
            RequestFactory.Create(body: new { shelfId = "shelf-1" }, method: "POST"),
            CancellationToken.None);

        Assert.Equal(500, Assert.IsType<ObjectResult>(result).StatusCode);
    }

    // ── Static helper tests ───────────────────────────────────────────────────

    [Theory]
    [InlineData("seed-a")]
    [InlineData("seed-b")]
    [InlineData("")]
    public void DeterministicColor_AlwaysReturnsSameColorForSameSeed(string seed)
    {
        var color1 = WallpaperFunctions.DeterministicColor(seed);
        var color2 = WallpaperFunctions.DeterministicColor(seed);

        Assert.Equal(color1, color2);
    }

    [Fact]
    public void DeterministicColor_ReturnsDifferentColorsForDifferentSeeds()
    {
        var colorA = WallpaperFunctions.DeterministicColor("seed-alpha-123");
        var colorB = WallpaperFunctions.DeterministicColor("seed-beta-456");

        // Different seeds should (very likely) produce different hues
        Assert.NotEqual(colorA, colorB);
    }

    [Theory]
    [InlineData(0f, 100f, 50f)]    // hue in [0,60)
    [InlineData(70f, 80f, 40f)]    // hue in [60,120)
    [InlineData(130f, 60f, 45f)]   // hue in [120,180)
    [InlineData(190f, 50f, 35f)]   // hue in [180,240)
    [InlineData(250f, 70f, 30f)]   // hue in [240,300)
    [InlineData(330f, 90f, 55f)]   // hue in [300,360)
    public void HslToSkColor_ReturnsNonBlackColor(float h, float s, float l)
    {
        var color = WallpaperFunctions.HslToSkColor(h, s, l);

        // Verify the result is a valid (non-black) colour for saturated inputs
        Assert.True(color.Red > 0 || color.Green > 0 || color.Blue > 0,
            $"HSL({h},{s},{l}) should not be black");
    }

    [Theory]
    [InlineData("#FF0000", 255, 0, 0)]
    [InlineData("#00FF00", 0, 255, 0)]
    [InlineData("#0000FF", 0, 0, 255)]
    [InlineData("#ffffff", 255, 255, 255)]
    public void ParseHexColor_ParsesKnownColors(string hex, byte r, byte g, byte b)
    {
        var color = WallpaperFunctions.ParseHexColor(hex);

        Assert.Equal(r, color.Red);
        Assert.Equal(g, color.Green);
        Assert.Equal(b, color.Blue);
    }

    [Fact]
    public void ParseHexColor_WithInvalidHex_FallsBackToWhite()
    {
        var color = WallpaperFunctions.ParseHexColor("not-a-color");

        Assert.Equal(SKColors.White, color);
    }

    [Fact]
    public void Truncate_WithLongString_AddsEllipsis()
    {
        var result = WallpaperFunctions.Truncate("A very long title that exceeds the limit", 10);

        Assert.True(result.Length <= 10);
        Assert.EndsWith("\u2026", result);
    }

    [Fact]
    public void Truncate_WithShortString_ReturnsUnchanged()
    {
        var result = WallpaperFunctions.Truncate("Short", 20);

        Assert.Equal("Short", result);
    }

    [Fact]
    public void CreateThumbnail_ProducesJpegBytes()
    {
        // Create a tiny 100x100 white PNG using SkiaSharp
        using var surface = SKSurface.Create(new SKImageInfo(100, 100, SKColorType.Rgba8888));
        surface.Canvas.Clear(SKColors.White);
        using var image = surface.Snapshot();
        using var pngData = image.Encode(SKEncodedImageFormat.Png, 100);
        var pngBytes = pngData.ToArray();

        var thumbBytes = WallpaperFunctions.CreateThumbnail(pngBytes, 50, 100, 100);

        Assert.NotNull(thumbBytes);
        Assert.NotEmpty(thumbBytes);
        // JPEG magic bytes: FF D8 FF
        Assert.Equal(0xFF, thumbBytes[0]);
        Assert.Equal(0xD8, thumbBytes[1]);
        Assert.Equal(0xFF, thumbBytes[2]);
    }
}
