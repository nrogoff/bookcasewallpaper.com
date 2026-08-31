namespace BookshelfWallpaper.Api.Models;

public class WallpaperGenerationResult
{
    public string ImageUrl { get; set; } = string.Empty;
    public string ThumbnailUrl { get; set; } = string.Empty;
    public string Format { get; set; } = "wallpaper";
}
