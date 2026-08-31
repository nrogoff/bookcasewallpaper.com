namespace BookshelfWallpaper.Api.Models;

public class BookshelfSettings
{
    public int Width { get; set; } = 1920;
    public int Height { get; set; } = 1080;
    public string ShelfColor { get; set; } = "#8B4513";
    public string WallColor { get; set; } = "#F5DEB3";
    public int ShelfCount { get; set; } = 4;
    public int BooksPerShelf { get; set; } = 20;
    public bool ShowTitles { get; set; }
    public string Format { get; set; } = "wallpaper";
}
