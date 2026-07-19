namespace BookshelfWallpaper.Api.Models;

public class Book
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = "Unknown";
    public string? CoverUrl { get; set; }
    public string? SpineUrl { get; set; }
    public string? SpineColor { get; set; }
    public string? SpineTextColor { get; set; }
    public string Source { get; set; } = "manual"; // audible | manual | upload
    public string? Asin { get; set; }
    public string AddedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
