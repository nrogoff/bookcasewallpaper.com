namespace BookshelfWallpaper.Api.Models;

public class AudibleConnection
{
    public string Id { get; set; } = string.Empty;
    public string UserId { get; set; } = string.Empty;
    public string AccessToken { get; set; } = string.Empty;
    public string? RefreshToken { get; set; }
    public string? TokenType { get; set; }
    public string? ExpiresAt { get; set; }
    public string Marketplace { get; set; } = "UK";
    public string CreatedAt { get; set; } = DateTime.UtcNow.ToString("O");
    public string UpdatedAt { get; set; } = DateTime.UtcNow.ToString("O");
}
