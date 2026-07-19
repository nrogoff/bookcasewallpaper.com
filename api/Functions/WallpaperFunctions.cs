using BookshelfWallpaper.Api.Models;
using BookshelfWallpaper.Api.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Cosmos;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.Logging;
using SkiaSharp;
using System.Text.Json;

namespace BookshelfWallpaper.Api.Functions;

public sealed class WallpaperFunctions
{
    private readonly ICosmosDbService _cosmos;
    private readonly IBlobStorageService _blobStorage;
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<WallpaperFunctions> _logger;

    private const int BookWidth = 40;
    private const int BookMargin = 3;
    private const int ShelfHeight = 12;
    private const int ShelfPaddingTop = 20;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
    };

    public WallpaperFunctions(ICosmosDbService cosmos, IBlobStorageService blobStorage, IHttpClientFactory httpClientFactory, ILogger<WallpaperFunctions> logger)
    {
        _cosmos = cosmos;
        _blobStorage = blobStorage;
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    private static string GetUserId(HttpRequest req)
        => req.Headers.TryGetValue("x-ms-client-principal-id", out var v) && !string.IsNullOrEmpty(v) ? v.ToString() : "anonymous";

    [Function("generateWallpaper")]
    public async Task<IActionResult> GenerateWallpaper(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "generateWallpaper")] HttpRequest req,
        CancellationToken cancellationToken)
    {
        _logger.LogInformation("generateWallpaper triggered");
        try
        {
            var userId = GetUserId(req);
            var body = await JsonSerializer.DeserializeAsync<GenerateWallpaperRequest>(req.Body, JsonOptions, cancellationToken);
            if (string.IsNullOrEmpty(body?.ShelfId))
                return new BadRequestObjectResult(new { error = "shelfId is required" });

            var container = await _cosmos.GetBookshelvesContainerAsync(cancellationToken);
            Bookshelf shelf;
            try
            {
                var resp = await container.ReadItemAsync<Bookshelf>(body.ShelfId, new PartitionKey(userId), cancellationToken: cancellationToken);
                shelf = resp.Resource;
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                return new NotFoundObjectResult(new { error = "Bookshelf not found" });
            }

            // Merge settings
            var settings = shelf.Settings;
            if (body.Settings is not null)
            {
                if (body.Settings.Width > 0) settings.Width = body.Settings.Width;
                if (body.Settings.Height > 0) settings.Height = body.Settings.Height;
                if (!string.IsNullOrEmpty(body.Settings.ShelfColor)) settings.ShelfColor = body.Settings.ShelfColor;
                if (!string.IsNullOrEmpty(body.Settings.WallColor)) settings.WallColor = body.Settings.WallColor;
                if (body.Settings.ShelfCount > 0) settings.ShelfCount = body.Settings.ShelfCount;
                if (body.Settings.BooksPerShelf > 0) settings.BooksPerShelf = body.Settings.BooksPerShelf;
                settings.ShowTitles = body.Settings.ShowTitles;
            }

            var imageBytes = await RenderWallpaperAsync(shelf.Books, settings, cancellationToken);
            var blobName = $"wallpapers/{userId}/{body.ShelfId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}.png";
            var imageUrl = await _blobStorage.UploadBufferAsync("wallpapers", blobName, imageBytes, "image/png", cancellationToken);

            var thumbBytes = CreateThumbnail(imageBytes, 480, settings.Width, settings.Height);
            var thumbBlobName = $"wallpapers/{userId}/{body.ShelfId}-{DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()}-thumb.jpg";
            var thumbnailUrl = await _blobStorage.UploadBufferAsync("wallpapers", thumbBlobName, thumbBytes, "image/jpeg", cancellationToken);

            return new OkObjectResult(new WallpaperGenerationResult
            {
                ImageUrl = imageUrl,
                ThumbnailUrl = thumbnailUrl,
                Format = settings.Format,
            });
        }
        catch (JsonException)
        {
            return new BadRequestObjectResult(new { error = "Invalid or missing request body" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "generateWallpaper error");
            return new ObjectResult(new { error = "Failed to generate wallpaper" }) { StatusCode = 500 };
        }
    }

    private async Task<byte[]> RenderWallpaperAsync(List<Book> books, BookshelfSettings settings, CancellationToken cancellationToken)
    {
        var width = settings.Width;
        var height = settings.Height;
        var httpClient = _httpClientFactory.CreateClient();

        using var surface = SKSurface.Create(new SKImageInfo(width, height, SKColorType.Rgba8888));
        var canvas = surface.Canvas;

        canvas.Clear(ParseHexColor(settings.WallColor));

        var rowHeight = height / settings.ShelfCount;
        var bookHeight = rowHeight - ShelfHeight - ShelfPaddingTop * 2;

        for (int row = 0; row < settings.ShelfCount; row++)
        {
            var rowBooks = books.Skip(row * settings.BooksPerShelf).Take(settings.BooksPerShelf).ToList();
            var rowY = row * rowHeight;

            for (int i = 0; i < rowBooks.Count; i++)
            {
                var book = rowBooks[i];
                var bookX = 40 + i * (BookWidth + BookMargin);
                var bookY = rowY + ShelfPaddingTop;
                var imageUrl = book.SpineUrl ?? book.CoverUrl;

                if (imageUrl is not null)
                {
                    try
                    {
                        var imgBytes = await httpClient.GetByteArrayAsync(imageUrl, cancellationToken);
                        using var bitmap = SKBitmap.Decode(imgBytes);
                        if (bitmap is not null)
                        {
                            canvas.DrawBitmap(bitmap, new SKRect(bookX, bookY, bookX + BookWidth, bookY + bookHeight));
                            continue;
                        }
                    }
                    catch { /* Fall through to placeholder */ }
                }

                DrawFallbackSpine(canvas, book, bookX, bookY, bookHeight, settings.ShowTitles);
            }

            // Shelf board
            var shelfY = rowY + rowHeight - ShelfHeight;
            using var shelfPaint = new SKPaint { Color = ParseHexColor(settings.ShelfColor), IsAntialias = false };
            canvas.DrawRect(20, shelfY, width - 40, ShelfHeight, shelfPaint);

            // Shadow
            using var shadowPaint = new SKPaint { Color = new SKColor(0, 0, 0, 38), IsAntialias = false };
            canvas.DrawRect(20, shelfY + ShelfHeight, width - 40, 4, shadowPaint);
        }

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    internal static byte[] CreateThumbnail(byte[] pngBytes, int thumbWidth, int originalWidth, int originalHeight)
    {
        var thumbHeight = (int)Math.Round((double)thumbWidth / originalWidth * originalHeight);
        using var original = SKBitmap.Decode(pngBytes);
        using var resized = original.Resize(new SKSizeI(thumbWidth, thumbHeight), new SKSamplingOptions(SKFilterMode.Linear));
        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, 90);
        return data.ToArray();
    }

    internal static void DrawFallbackSpine(SKCanvas canvas, Book book, int x, int y, int height, bool showTitles)
    {
        var color = string.IsNullOrEmpty(book.SpineColor)
            ? DeterministicColor(book.Id)
            : ParseHexColor(book.SpineColor);

        using var paint = new SKPaint { Color = color, IsAntialias = false };
        canvas.DrawRect(x, y, BookWidth, height, paint);

        if (showTitles)
        {
            var textColor = string.IsNullOrEmpty(book.SpineTextColor)
                ? SKColors.White
                : ParseHexColor(book.SpineTextColor);

            using var font = new SKFont { Size = Math.Min(13f, BookWidth * 0.3f) };
            using var textPaint = new SKPaint { Color = textColor, IsAntialias = true };

            var text = Truncate(book.Title, 20);
            canvas.Save();
            canvas.Translate(x + BookWidth / 2f, y + height / 2f);
            canvas.RotateDegrees(-90);
            canvas.DrawText(text, 0, 0, SKTextAlign.Center, font, textPaint);
            canvas.Restore();
        }
    }

    internal static SKColor DeterministicColor(string seed)
    {
        int hash = 0;
        foreach (char c in seed)
            hash = c + ((hash << 5) - hash);
        var h = Math.Abs(hash) % 360;
        return HslToSkColor(h, 45f, 35f);
    }

    internal static SKColor HslToSkColor(float h, float s, float l)
    {
        s /= 100f;
        l /= 100f;
        float c = (1f - Math.Abs(2f * l - 1f)) * s;
        float x = c * (1f - Math.Abs((h / 60f) % 2f - 1f));
        float m = l - c / 2f;
        float r, g, b;
        if (h < 60) { r = c; g = x; b = 0; }
        else if (h < 120) { r = x; g = c; b = 0; }
        else if (h < 180) { r = 0; g = c; b = x; }
        else if (h < 240) { r = 0; g = x; b = c; }
        else if (h < 300) { r = x; g = 0; b = c; }
        else { r = c; g = 0; b = x; }
        return new SKColor((byte)((r + m) * 255), (byte)((g + m) * 255), (byte)((b + m) * 255));
    }

    internal static SKColor ParseHexColor(string hex)
    {
        if (SKColor.TryParse(hex, out var color)) return color;
        return SKColors.White;
    }

    internal static string Truncate(string s, int max) => s.Length > max ? s[..(max - 1)] + "\u2026" : s;
}

file sealed class GenerateWallpaperRequest
{
    public string? ShelfId { get; set; }
    public BookshelfSettings? Settings { get; set; }
}
