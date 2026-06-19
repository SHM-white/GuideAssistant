using CommunityToolkit.Mvvm.ComponentModel;

namespace GuideAssistant.Models;

public partial class TabItem : ObservableObject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();

    [ObservableProperty]
    private string _title = "新标签页";

    [ObservableProperty]
    private string _url = "https://www.bilibili.com";

    [ObservableProperty]
    private string? _faviconUrl;

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private bool _canGoBack;

    [ObservableProperty]
    private bool _canGoForward;

    [ObservableProperty]
    private bool _isBookmarked;

    public Stack<string> BackHistory { get; set; } = new();
    public Stack<string> ForwardHistory { get; set; } = new();
}
