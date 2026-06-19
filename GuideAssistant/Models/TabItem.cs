namespace GuideAssistant.Models;

public class TabItem
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = "新标签页";
    public string Url { get; set; } = "https://www.bilibili.com";
    public string? FaviconUrl { get; set; }
    public bool IsLoading { get; set; }
    public bool CanGoBack { get; set; }
    public bool CanGoForward { get; set; }
    public bool IsBookmarked { get; set; }
    public Stack<string> BackHistory { get; set; } = new();
    public Stack<string> ForwardHistory { get; set; } = new();
}
