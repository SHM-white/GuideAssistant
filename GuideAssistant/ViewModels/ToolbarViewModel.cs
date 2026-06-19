using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GuideAssistant.Models;
using GuideAssistant.Services;
using Serilog;

namespace GuideAssistant.ViewModels;

public partial class ToolbarViewModel : ObservableObject
{
    private readonly TabManager _tabManager;
    private readonly BookmarkService _bookmarkService;
    private readonly GameDetector _gameDetector;

    private string _urlBarText = "https://www.bilibili.com";
    private bool _isCurrentBookmarked;
    private string _bookmarkSearchText = "";

    public string UrlBarText { get => _urlBarText; set => SetProperty(ref _urlBarText, value); }
    public bool IsCurrentBookmarked { get => _isCurrentBookmarked; set => SetProperty(ref _isCurrentBookmarked, value); }
    public string BookmarkSearchText { get => _bookmarkSearchText; set => SetProperty(ref _bookmarkSearchText, value); }

    public IRelayCommand<string> NavigateCommand { get; }
    public IRelayCommand NavigateBackCommand { get; }
    public IRelayCommand NavigateForwardCommand { get; }
    public IRelayCommand NavigateRefreshCommand { get; }
    public IRelayCommand<TabItem> SelectTabCommand { get; }
    public IRelayCommand AddNewTabCommand { get; }
    public IRelayCommand<TabItem> CloseTabItemCommand { get; }
    public IRelayCommand<TabItem> QuickBookmarkTabCommand { get; }
    public IRelayCommand BookmarkCurrentPageCommand { get; }
    public IRelayCommand<string> SearchBookmarksCommand { get; }
    public IRelayCommand OpenSettingsCommand { get; }

    public ToolbarViewModel(TabManager tabManager, BookmarkService bookmarkService, GameDetector gameDetector)
    {
        _tabManager = tabManager;
        _bookmarkService = bookmarkService;
        _gameDetector = gameDetector;

        if (_tabManager.ActiveTab != null)
            _urlBarText = _tabManager.ActiveTab.Url;

        _bookmarkService.BookmarksChanged += () => IsCurrentBookmarked = _bookmarkService.IsUrlBookmarked(_tabManager.ActiveTab?.Url ?? "");

        NavigateCommand = new RelayCommand<string>(Navigate);
        NavigateBackCommand = new RelayCommand(() => MainViewModel_RequestAction(WebViewAction.GoBack));
        NavigateForwardCommand = new RelayCommand(() => MainViewModel_RequestAction(WebViewAction.GoForward));
        NavigateRefreshCommand = new RelayCommand(() => MainViewModel_RequestAction(WebViewAction.Refresh));
        SelectTabCommand = new RelayCommand<TabItem>(SelectTab);
        AddNewTabCommand = new RelayCommand(AddNewTab);
        CloseTabItemCommand = new RelayCommand<TabItem>(CloseTabItem);
        QuickBookmarkTabCommand = new RelayCommand<TabItem>(QuickBookmarkTab);
        BookmarkCurrentPageCommand = new RelayCommand(BookmarkCurrentPage);
        SearchBookmarksCommand = new RelayCommand<string>(SearchBookmarks);
        OpenSettingsCommand = new RelayCommand(OpenSettings);
    }

    public event Action<string>? NavigateToUrlRequested;
    public event Action<WebViewAction>? WebViewActionRequested;
    public event Action? NewTabRequested;
    public event Action<string>? CloseTabRequested;
    public event Action? BookmarkChanged;
    public event Action? SettingsRequested;
    public event Action<TabItem>? TabSwitched;
    public event Action? BookmarksRefreshRequested;

    private void Navigate(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return;
        input = input.Trim();
        if (!IsLikelyUrl(input)) input = $"https://www.bing.com/search?q={Uri.EscapeDataString(input)}";
        else if (!input.StartsWith("http://") && !input.StartsWith("https://")) input = $"https://{input}";
        UrlBarText = input;
        NavigateToUrlRequested?.Invoke(input);
    }

    private static bool IsLikelyUrl(string input)
    {
        if (input.StartsWith("http://") || input.StartsWith("https://")) return true;
        if (input.Contains(' ')) return false;
        if (input.Any(c => c >= 0x4E00 && c <= 0x9FFF)) return false;
        if (input.Contains('.')) return true;
        if (input.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) return true;
        if (Uri.TryCreate($"https://{input}", UriKind.Absolute, out var uri))
            return uri.Host.Contains('.') || uri.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase);
        return false;
    }

    private void MainViewModel_RequestAction(WebViewAction action) => WebViewActionRequested?.Invoke(action);

    private void SelectTab(TabItem? tab)
    {
        if (tab == null) return;
        _tabManager.ActiveTab = tab;
        UrlBarText = tab.Url;
        IsCurrentBookmarked = _bookmarkService.IsUrlBookmarked(tab.Url);
        TabSwitched?.Invoke(tab);
        NavigateToUrlRequested?.Invoke(tab.Url);
    }

    private void AddNewTab()
    {
        var tab = _tabManager.AddTab();
        SelectTab(tab);
        NewTabRequested?.Invoke();
    }

    private void CloseTabItem(TabItem? tab)
    {
        if (tab == null) return;
        _tabManager.CloseTab(tab.Id);
        CloseTabRequested?.Invoke(tab.Id);
        if (_tabManager.ActiveTab != null) SelectTab(_tabManager.ActiveTab);
    }

    private void QuickBookmarkTab(TabItem? tab)
    {
        if (tab == null || _bookmarkService.IsUrlBookmarked(tab.Url)) return;
        int? gameId = ResolveForegroundGameId();
        _bookmarkService.QuickAdd(tab.Title, tab.Url, gameId: gameId);
        IsCurrentBookmarked = true;
        BookmarkChanged?.Invoke();
    }

    private void BookmarkCurrentPage()
    {
        var url = _tabManager.ActiveTab?.Url;
        if (string.IsNullOrEmpty(url) || _bookmarkService.IsUrlBookmarked(url)) return;
        var title = _tabManager.ActiveTab?.Title ?? url;
        int? gameId = ResolveForegroundGameId();
        _bookmarkService.QuickAdd(title, url, gameId: gameId);
        IsCurrentBookmarked = true;
        BookmarkChanged?.Invoke();
    }

    private void SearchBookmarks(string? query)
    {
        BookmarkSearchText = query ?? "";
        BookmarksRefreshRequested?.Invoke();
    }

    private void OpenSettings() => SettingsRequested?.Invoke();

    public void OnUrlChanged(string url)
    {
        UrlBarText = url;
        IsCurrentBookmarked = _bookmarkService.IsUrlBookmarked(url);
    }

    private int? ResolveForegroundGameId()
    {
        var gameName = _gameDetector.CurrentForegroundGameName;
        if (string.IsNullOrEmpty(gameName)) return null;
        var games = _bookmarkService.GetAllGames();
        return games.FirstOrDefault(g => string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase))?.Id;
    }
}
