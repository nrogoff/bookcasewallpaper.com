namespace BookshelfWallpaper.Api.Models;

public class AudibleSyncResult
{
    public int BooksFound { get; set; }
    public int BooksAdded { get; set; }
    public List<Book> Books { get; set; } = [];
}
