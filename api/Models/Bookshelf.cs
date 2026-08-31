namespace BookshelfWallpaper.Api.Models;

public class Bookshelf
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string UserId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public List<Book> Books { get; set; } = [];
    public BookshelfSettings Settings { get; set; } = new();
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
