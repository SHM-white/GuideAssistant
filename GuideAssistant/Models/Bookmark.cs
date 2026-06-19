namespace GuideAssistant.Models;

public class Bookmark
{
    public int Id { get; set; }
    public int? GameId { get; set; }
    public int? FolderId { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
    public string? FaviconUrl { get; set; }
    public string? Tags { get; set; }
    public string? Notes { get; set; }
    public bool IsFavorite { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public DateTime UpdatedAt { get; set; } = DateTime.Now;
    public string? GameName { get; set; }
}
