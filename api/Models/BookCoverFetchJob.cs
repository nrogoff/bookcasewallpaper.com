namespace BookshelfWallpaper.Api.Models;

public class BookCoverFetchJob
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string BookId { get; set; } = string.Empty;
    public string ShelfId { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Author { get; set; } = string.Empty;
    public string? Asin { get; set; }
    public string Status { get; set; } = "pending"; // pending | processing | done | failed
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
