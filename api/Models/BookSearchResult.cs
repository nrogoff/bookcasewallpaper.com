namespace BookshelfWallpaper.Api.Models;

public class BookSearchResult
{
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = "Unknown";
    public string? CoverUrl { get; set; }
    public string? Asin { get; set; }
    public string Source { get; set; } = "openlibrary";
}
