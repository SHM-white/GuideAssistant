using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.Messaging;
using GuideAssistant.Data;
using GuideAssistant.Models;
using GuideAssistant.Services;
using Serilog;

namespace GuideAssistant.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly TabManager _tabManager;
    private readonly HotkeyService _hotkeyService;
    private readonly WindowManager _windowManager;
    private readonly GameDetector _gameDetector;
    private readonly BookmarkService _bookmarkService;
    private readonly SubtitleService _subtitleService;
    private readonly DirectionService _directionService;
    private readonly HotkeyRepository _hotkeyRepository;
    private readonly WindowStateRepository _windowStateRepo;

    private double _opacity = 0.9;
    private string _currentUrl = "https://www.bilibili.com";
    private string _currentTitle = "新标签页";
    private bool _isLoading;
    private bool _isSubtitleEnabled;
    private bool _isMiniMapEnabled;
    private bool _isVisible = true;
    private bool _isCurrentUrlBookmarked;

    public double Opacity { get => _opacity; set => SetProperty(ref _opacity, value); }
    public string CurrentUrl { get => _currentUrl; set => SetProperty(ref _currentUrl, value); }
    public string CurrentTitle { get => _currentTitle; set => SetProperty(ref _currentTitle, value); }
    public bool IsLoading { get => _isLoading; set => SetProperty(ref _isLoading, value); }

    public bool IsSubtitleEnabled
    {
        get => _isSubtitleEnabled;
        set { if (SetProperty(ref _isSubtitleEnabled, value)) WeakReferenceMessenger.Default.Send(new OverlayToggleMessage("subtitle", value)); }
    }

    public bool IsMiniMapEnabled
    {
        get => _isMiniMapEnabled;
        set { if (SetProperty(ref _isMiniMapEnabled, value)) WeakReferenceMessenger.Default.Send(new OverlayToggleMessage("minimap", value)); }
    }

    public bool IsVisible
    {
        get => _isVisible;
        set { if (SetProperty(ref _isVisible, value)) WeakReferenceMessenger.Default.Send(new VisibilityChangedMessage(value)); }
    }

    public bool IsCurrentUrlBookmarked { get => _isCurrentUrlBookmarked; set => SetProperty(ref _isCurrentUrlBookmarked, value); }

    public IRelayCommand<string> NavigateToUrlCommand { get; }
    public IRelayCommand NavigateBackCommand { get; }
    public IRelayCommand NavigateForwardCommand { get; }
    public IRelayCommand NavigateRefreshCommand { get; }
    public IRelayCommand<string> SwitchToTabCommand { get; }
    public IRelayCommand AddTabCommand { get; }
    public IRelayCommand<string> CloseTabCommand { get; }
    public IRelayCommand AddBookmarkCommand { get; }

    public MainViewModel(
        TabManager tabManager, HotkeyService hotkeyService, WindowManager windowManager,
        GameDetector gameDetector, BookmarkService bookmarkService,
        SubtitleService subtitleService, DirectionService directionService,
        HotkeyRepository hotkeyRepository, WindowStateRepository windowStateRepo)
    {
        _tabManager = tabManager;
        _hotkeyService = hotkeyService;
        _windowManager = windowManager;
        _gameDetector = gameDetector;
        _bookmarkService = bookmarkService;
        _subtitleService = subtitleService;
        _directionService = directionService;
        _hotkeyRepository = hotkeyRepository;
        _windowStateRepo = windowStateRepo;

        if (_tabManager.ActiveTab != null)
        {
            _currentUrl = _tabManager.ActiveTab.Url;
            _currentTitle = _tabManager.ActiveTab.Title;
        }

        NavigateToUrlCommand = new RelayCommand<string>(NavigateToUrl);
        NavigateBackCommand = new RelayCommand(NavigateBack);
        NavigateForwardCommand = new RelayCommand(NavigateForward);
        NavigateRefreshCommand = new RelayCommand(NavigateRefresh);
        SwitchToTabCommand = new RelayCommand<string>(SwitchToTab);
        AddTabCommand = new RelayCommand(AddTab);
        CloseTabCommand = new RelayCommand<string>(CloseTab);
        AddBookmarkCommand = new RelayCommand(AddBookmark);
    }

    public TabManager TabManager => _tabManager;
    public SubtitleService SubtitleService => _subtitleService;
    public DirectionService DirectionService => _directionService;

    private void NavigateToUrl(string url)
    {
        if (_tabManager.ActiveTab == null) return;
        _tabManager.Navigate(_tabManager.ActiveTab, url);
        CurrentUrl = url;
        IsCurrentUrlBookmarked = _bookmarkService.IsUrlBookmarked(url);
        WeakReferenceMessenger.Default.Send(new WebViewNavigateMessage(url));
        if (url.Contains("bilibili.com/video/"))
            _ = _subtitleService.LoadSubtitle(url);
    }

    private void NavigateBack() => WeakReferenceMessenger.Default.Send(new WebViewActionMessage(WebViewAction.GoBack));
    private void NavigateForward() => WeakReferenceMessenger.Default.Send(new WebViewActionMessage(WebViewAction.GoForward));
    private void NavigateRefresh() => WeakReferenceMessenger.Default.Send(new WebViewActionMessage(WebViewAction.Refresh));

    private void SwitchToTab(string tabId)
    {
        var tab = _tabManager.Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab == null) return;
        _tabManager.ActiveTab = tab;
        CurrentUrl = tab.Url;
        CurrentTitle = tab.Title;
        IsCurrentUrlBookmarked = _bookmarkService.IsUrlBookmarked(tab.Url);
        WeakReferenceMessenger.Default.Send(new SwitchTabMessage(tab));
    }

    private void AddTab() { var tab = _tabManager.AddTab(); SwitchToTab(tab.Id); }

    private void CloseTab(string tabId)
    {
        _tabManager.CloseTab(tabId);
        WeakReferenceMessenger.Default.Send(new TabClosedMessage(tabId));
        if (_tabManager.ActiveTab != null)
        {
            CurrentUrl = _tabManager.ActiveTab.Url;
            CurrentTitle = _tabManager.ActiveTab.Title;
        }
    }

    private void AddBookmark()
    {
        var url = _tabManager.GetBookmarkUrl();
        var title = _tabManager.GetBookmarkTitle();
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(title)) return;
        if (_bookmarkService.IsUrlBookmarked(url)) return;
        int? gameId = ResolveForegroundGameId();
        _bookmarkService.QuickAdd(title, url, gameId: gameId);
        IsCurrentUrlBookmarked = true;
        WeakReferenceMessenger.Default.Send(new BookmarksChangedMessage());
    }

    public async void HandleHotkeyAction(string action)
    {
        switch (action)
        {
            case "play_pause": await ExecuteWebScript("window.__gv_player.togglePlay()"); break;
            case "fast_forward": await ExecuteWebScript("window.__gv_player.fastForward(10)"); break;
            case "fast_backward": await ExecuteWebScript("window.__gv_player.fastBackward(10)"); break;
            case "volume_up": await ExecuteWebScript("window.__gv_player.volumeUp()"); break;
            case "volume_down": await ExecuteWebScript("window.__gv_player.volumeDown()"); break;
            case "toggle_visibility": IsVisible = !IsVisible; break;
            case "bookmark_page": AddBookmark(); break;
            case "toggle_subtitle": IsSubtitleEnabled = !IsSubtitleEnabled; break;
            case "toggle_minimap": IsMiniMapEnabled = !IsMiniMapEnabled; break;
        }
    }

    private async Task<string> ExecuteWebScript(string script)
    {
        var tcs = new TaskCompletionSource<string>();
        WeakReferenceMessenger.Default.Send(new ExecuteScriptRequestMessage(script, tcs));
        return await tcs.Task;
    }

    public void InitializeHotkeys()
    {
        _hotkeyService.HotkeyTriggered += HandleHotkeyAction;
        LoadHotkeyBindings();
        _hotkeyService.Start();
    }

    public void ReloadHotkeys()
    {
        LoadHotkeyBindings();
        Log.Information("Hotkeys reloaded from profile");
    }

    /// <summary>
    /// Load hotkey bindings from the database profile.
    /// EnsureDefaultHotkeys guarantees every known action has a binding in DB.
    /// </summary>
    private void LoadHotkeyBindings()
    {
        var profile = _hotkeyRepository.GetDefaultProfile();
        var bindings = profile?.Bindings
            ?.Where(b => b.VirtualKey != 0)
            .ToList() ?? new();

        _hotkeyService.SetBindings(bindings);
    }

    public void InitializeSubtitleSync()
    {
        _subtitleService.DirectionWordDetected += OnDirectionWordDetected;
    }

    private void OnDirectionWordDetected(string word)
    {
        WeakReferenceMessenger.Default.Send(new DirectionWordMessage(word));
    }

    public WindowState? LoadWindowState(string name) => _windowStateRepo.Get(name);

    public void SaveWindowState(double x, double y, double w, double h)
    {
        _windowStateRepo.Save(new WindowState
        {
            WindowName = "MainWindow", X = x, Y = y, Width = w, Height = h,
            Opacity = Opacity, IsAlwaysOnTop = true
        });
    }

    public void UpdateBookmarkState(string url) => IsCurrentUrlBookmarked = _bookmarkService.IsUrlBookmarked(url);

    public void Cleanup()
    {
        _hotkeyService.HotkeyTriggered -= HandleHotkeyAction;
        _hotkeyService.Dispose();
        _gameDetector.Dispose();
        _subtitleService.Dispose();
        _tabManager.Cleanup();
    }

    private int? ResolveForegroundGameId()
    {
        var gameName = _gameDetector.CurrentForegroundGameName;
        if (string.IsNullOrEmpty(gameName)) return null;
        var games = _bookmarkService.GetAllGames();
        return games.FirstOrDefault(g => string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase))?.Id;
    }
}

// ── Messages ──────────────────────────────────────────

public sealed class NavigateToUrlMessage { public string Url { get; } public NavigateToUrlMessage(string u) => Url = u; }
public sealed class WebViewNavigateMessage { public string Url { get; } public WebViewNavigateMessage(string u) => Url = u; }
public sealed class WebViewActionMessage { public WebViewAction Action { get; } public WebViewActionMessage(WebViewAction a) => Action = a; }
public enum WebViewAction { GoBack, GoForward, Refresh }
public sealed class SwitchTabMessage { public TabItem Tab { get; } public SwitchTabMessage(TabItem t) => Tab = t; }
public sealed class TabClosedMessage { public string TabId { get; } public TabClosedMessage(string id) => TabId = id; }
public sealed class BookmarksChangedMessage { }
public sealed class VisibilityChangedMessage { public bool IsVisible { get; } public VisibilityChangedMessage(bool v) => IsVisible = v; }
public sealed class ExecuteScriptRequestMessage { public string Script { get; } public TaskCompletionSource<string> Tcs { get; } public ExecuteScriptRequestMessage(string s, TaskCompletionSource<string> t) { Script = s; Tcs = t; } }
public sealed class DirectionWordMessage { public string Word { get; } public DirectionWordMessage(string w) => Word = w; }
public sealed class HotkeysReloadMessage { }
public sealed class OverlayToggleMessage { public string Type { get; } public bool Enabled { get; } public OverlayToggleMessage(string t, bool e) { Type = t; Enabled = e; } }
public sealed class SubtitleSyncMessage { public bool Start { get; } public SubtitleSyncMessage(bool s) => Start = s; }
public sealed class OpacityChangedMessage { public double Value { get; } public OpacityChangedMessage(double v) => Value = v; }
public sealed class OpenSettingsMessage { }
public sealed class RefreshBookmarksMessage { }
