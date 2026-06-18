using GuideAssistant.Controls;
using GuideAssistant.Data;
using GuideAssistant.Helpers;
using GuideAssistant.Models;
using GuideAssistant.Services;
using GuideAssistant.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Serilog;
using WinRT.Interop;

namespace GuideAssistant;

public sealed partial class MainWindow : Window
{
    private readonly TabManager _tabManager;
    private readonly HotkeyService _hotkeyService;
    private readonly WindowManager _windowManager;
    private readonly GameDetector _gameDetector;
    private readonly BookmarkService _bookmarkService;
    private readonly SubtitleService _subtitleService;
    private readonly BilibiliApi _bilibiliApi;
    private readonly DirectionService _directionService;
    private readonly HotkeyRepository _hotkeyRepository;
    private readonly WindowStateRepository _windowStateRepo;

    private ToolbarWindow? _toolbarWindow;
    private SubtitleOverlay? _subtitleOverlay;
    private MiniMapOverlay? _miniMapOverlay;
    private IntPtr _hwnd;
    private System.Timers.Timer? _subtitleTimeSyncTimer;

    private bool _isVisible = true;
    private bool _isSubtitleEnabled;
    private bool _isMiniMapEnabled;

    // Events for ToolbarWindow to subscribe to
    public event Action<string>? UrlChanged;
    public event Action<string>? TitleChanged;
    public event Action<bool>? LoadingStateChanged;

    public MainWindow(
        TabManager tabManager,
        HotkeyService hotkeyService,
        WindowManager windowManager,
        GameDetector gameDetector,
        BookmarkService bookmarkService,
        SubtitleService subtitleService,
        BilibiliApi bilibiliApi,
        DirectionService directionService,
        HotkeyRepository hotkeyRepository,
        WindowStateRepository windowStateRepo)
    {
        InitializeComponent();

        _tabManager = tabManager;
        _hotkeyService = hotkeyService;
        _windowManager = windowManager;
        _gameDetector = gameDetector;
        _bookmarkService = bookmarkService;
        _subtitleService = subtitleService;
        _bilibiliApi = bilibiliApi;
        _directionService = directionService;
        _hotkeyRepository = hotkeyRepository;
        _windowStateRepo = windowStateRepo;

        // Refresh bookmarks panel when bookmarks change
        _bookmarkService.BookmarksChanged += () =>
        {
            if (BookmarksPanel.Visibility == Visibility.Visible)
                DispatcherQueue.TryEnqueue(RefreshBookmarksPanel);
        };

        // Show bookmarks panel by default
        RefreshBookmarksPanel();
        BookmarksPanel.Visibility = Visibility.Visible;

        _hwnd = WindowNative.GetWindowHandle(this);
        _windowManager.MainWindowHandle = _hwnd;

        Closed += MainWindow_Closed;

        InitializeWindow();
        InitializeControls();
        InitializeHotkeys();
        InitializeSubtitle();
        RestoreWindowState();

        // Start game detection for auto-launching helpers
        _gameDetector.Start();

        // Create and show the ToolbarWindow
        try
        {
            _toolbarWindow = App.Services.GetRequiredService<ToolbarWindow>();
            _toolbarWindow.SetMainWindow(this);
            _toolbarWindow.Activate();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to create ToolbarWindow");
        }
    }

    private void InitializeWindow()
    {
        var titleBar = AppWindow.TitleBar;
        titleBar.ExtendsContentIntoTitleBar = true;
        titleBar.PreferredHeightOption = TitleBarHeightOption.Tall;

        // Make system title bar elements fully transparent
        titleBar.ButtonBackgroundColor = Colors.Transparent;
        titleBar.ButtonForegroundColor = Colors.Transparent;
        titleBar.ButtonInactiveBackgroundColor = Colors.Transparent;
        titleBar.ButtonInactiveForegroundColor = Colors.Transparent;
        titleBar.ButtonHoverBackgroundColor = Colors.Transparent;
        titleBar.ButtonHoverForegroundColor = Colors.Transparent;
        titleBar.ButtonPressedBackgroundColor = Colors.Transparent;
        titleBar.ButtonPressedForegroundColor = Colors.Transparent;

        // Remove system title text and icon
        AppWindow.Title = "";

        // Set drag region: top 48px area is draggable (extends above the 6px visual bar)
        titleBar.SetDragRectangles(new Windows.Graphics.RectInt32[]
        {
            new Windows.Graphics.RectInt32 { X = 0, Y = 0, Width = 100000, Height = 48 }
        });

        var presenter = AppWindow.Presenter as OverlappedPresenter;
        if (presenter != null)
            presenter.IsAlwaysOnTop = true;

        AppWindow.Show();

        // Hide system caption buttons — call immediately + after activation + delayed retry
        DispatcherQueue.TryEnqueue(() =>
        {
            Helpers.Win32Helper.HideSystemCaptionButtons(_hwnd);
        });
        Activated += (s, e) =>
        {
            Helpers.Win32Helper.HideSystemCaptionButtons(_hwnd);
        };

        Log.Information("MainWindow initialized");
    }

    private void InitializeControls()
    {
        // Wire floating window buttons
        MinimizeBtn.Click += (s, e) =>
            (AppWindow.Presenter as OverlappedPresenter)?.Minimize();
        MaximizeBtn.Click += (s, e) =>
        {
            var p = AppWindow.Presenter as OverlappedPresenter;
            if (p != null)
            {
                if (p.State == OverlappedPresenterState.Maximized) p.Restore();
                else p.Maximize();
            }
        };
        CloseBtn.Click += (s, e) => Close();
        OpacityBtn.Click += (s, e) =>
        {
            OpacitySlider.Visibility = OpacitySlider.Visibility == Visibility.Visible
                ? Visibility.Collapsed
                : Visibility.Visible;
        };
        OpacitySlider.ValueChanged += (s, e) =>
            _windowManager.SetOpacity(_hwnd, e.NewValue);

        // Hover transparency for window buttons
        ApplyHoverTransparency(OpacityBtn);
        ApplyHoverTransparency(MinimizeBtn);
        ApplyHoverTransparency(MaximizeBtn);
        ApplyHoverTransparency(CloseBtn);

        WebViewControl.Initialize(_tabManager);
        WebViewControl.TitleChanged += (title) => TitleChanged?.Invoke(title);
        WebViewControl.UrlChanged += (url) =>
        {
            UrlChanged?.Invoke(url);
            if (url.Contains("bilibili.com/video/"))
                _ = _subtitleService.LoadSubtitle(url);
        };
        WebViewControl.LoadingStateChanged += (isLoading) => LoadingStateChanged?.Invoke(isLoading);

        if (_tabManager.ActiveTab != null)
            SwitchToTab(_tabManager.ActiveTab);
    }

    private static void ApplyHoverTransparency(Button btn)
    {
        btn.Opacity = 0.5;
        btn.PointerEntered += (s, e) => btn.Opacity = 1.0;
        btn.PointerExited += (s, e) => btn.Opacity = 0.5;
    }

    private void SwitchToTab(TabItem tab)
    {
        WebViewControl.LoadUrl(tab, tab.Url);
        UrlChanged?.Invoke(tab.Url);
    }

    public void SwitchToTabById(string tabId)
    {
        var tab = _tabManager.Tabs.FirstOrDefault(t => t.Id == tabId);
        if (tab != null)
        {
            _tabManager.ActiveTab = tab;
            SwitchToTab(tab);
        }
    }

    public void RemoveWebViewForTab(string tabId)
    {
        WebViewControl.RemoveWebView(tabId);
    }

    // --- Public API for ToolbarWindow ---

    public void NavigateToUrl(string url)
    {
        if (_tabManager.ActiveTab == null) return;
        // Ensure the active tab's WebView is displayed before navigating
        SwitchToTab(_tabManager.ActiveTab);
        _tabManager.Navigate(_tabManager.ActiveTab, url);
        WebViewControl.Navigate(url);
    }

    public void NavigateBack() => WebViewControl.GoBack();

    public void NavigateForward() => WebViewControl.GoForward();

    public void NavigateRefresh() => WebViewControl.Refresh();

    public string? GetCurrentUrl() => _tabManager.ActiveTab?.Url;

    public string? GetCurrentTitle() => _tabManager.ActiveTab?.Title;

    public bool IsCurrentUrlBookmarked()
    {
        var url = _tabManager.ActiveTab?.Url;
        return url != null && _bookmarkService.IsUrlBookmarked(url);
    }

    public void ToggleBookmarksPanel()
    {
        if (BookmarksPanel.Visibility == Visibility.Visible)
            BookmarksPanel.Visibility = Visibility.Collapsed;
        else
        {
            RefreshBookmarksPanel();
            BookmarksPanel.Visibility = Visibility.Visible;
        }
    }

    private void RefreshBookmarksPanel()
    {
        BookmarksTreeView.RootNodes.Clear();

        var allBookmarks = _bookmarkService.GetAll();
        var allGames = _bookmarkService.GetAllGames();
        var gameLookup = allGames.ToDictionary(g => g.Id, g => g.GameName);

        // Group bookmarks by game, ungrouped go to "未分类"
        var grouped = allBookmarks
            .GroupBy(b => b.GameId.HasValue && gameLookup.ContainsKey(b.GameId.Value)
                ? gameLookup[b.GameId.Value]
                : "未分类")
            .OrderBy(g => g.Key == "未分类" ? 1 : 0)
            .ThenBy(g => g.Key);

        foreach (var group in grouped)
        {
            var folderNode = new TreeViewNode
            {
                Content = $"📁 {group.Key}  ({group.Count()})",
                IsExpanded = true
            };

            foreach (var bm in group)
            {
                var itemNode = new TreeViewNode
                {
                    Content = bm
                };
                folderNode.Children.Add(itemNode);
            }

            BookmarksTreeView.RootNodes.Add(folderNode);
        }
    }

    private void BookmarksTreeView_ItemInvoked(TreeView sender, TreeViewItemInvokedEventArgs args)
    {
        if (args.InvokedItem is Bookmark bookmark)
        {
            NavigateToUrl(bookmark.Url);
        }
    }

    private void BookmarksPanelClose_Click(object sender, RoutedEventArgs e)
    {
        BookmarksPanel.Visibility = Visibility.Collapsed;
    }

    private void BookmarksToggleBtn_Click(object sender, RoutedEventArgs e)
    {
        ToggleBookmarksPanel();
    }

    public async Task<string> ExecuteWebScript(string script)
        => await WebViewControl.ExecuteScript(script);

    public void AddBookmark()
    {
        var url = _tabManager.GetBookmarkUrl();
        var title = _tabManager.GetBookmarkTitle();
        if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(title))
        {
            int? gameId = ResolveForegroundGameId();
            _bookmarkService.QuickAdd(title, url, gameId: gameId);
        }
    }

    private int? ResolveForegroundGameId()
    {
        var gameName = _gameDetector.CurrentForegroundGameName;
        if (string.IsNullOrEmpty(gameName)) return null;
        var games = _bookmarkService.GetAllGames();
        return games.FirstOrDefault(g =>
            string.Equals(g.GameName, gameName, StringComparison.OrdinalIgnoreCase))?.Id;
    }

    public void ShowBookmarkSearch(string keyword)
    {
        var results = _bookmarkService.Search(keyword);
        if (results.Count > 0 && _tabManager.ActiveTab != null)
        {
            _tabManager.Navigate(_tabManager.ActiveTab, results[0].Url);
            WebViewControl.Navigate(results[0].Url);
        }
        else if (results.Count == 0)
        {
            Log.Information("Bookmark search returned no results for: {Keyword}", keyword);
        }
    }

    public void ShowSettingsPage()
    {
        var settingsWindow = new SettingsWindow(
            _hotkeyRepository,
            _hotkeyService,
            _isSubtitleEnabled,
            _isMiniMapEnabled,
            onHotkeysChanged: ReloadHotkeys,
            onSubtitleToggled: (enabled) =>
            {
                if (enabled != _isSubtitleEnabled)
                    HandleHotkeyAction("toggle_subtitle");
            },
            onMinimapToggled: (enabled) =>
            {
                if (enabled != _isMiniMapEnabled)
                    HandleHotkeyAction("toggle_minimap");
            },
            getOpacity: () => OpacitySlider.Value,
            setOpacity: (val) =>
            {
                OpacitySlider.Value = val;
                _windowManager.SetOpacity(_hwnd, val);
            });
        settingsWindow.Activate();
    }

    private void ReloadHotkeys()
    {
        // Unregister all current hotkeys
        foreach (var id in _hotkeyService.GetRegisteredIds())
            _hotkeyService.UnregisterHotkey(id);

        // Re-register from saved profile
        var profile = _hotkeyRepository.GetDefaultProfile();
        if (profile?.Bindings.Count > 0)
        {
            foreach (var b in profile.Bindings)
            {
                if (b.VirtualKey != 0)
                    _hotkeyService.RegisterHotkey(b.ActionName, b.Modifiers, b.VirtualKey, () => HandleHotkeyAction(b.ActionName));
            }
        }

        // Update the low-level hook mapping dynamically — force a restart
        _hotkeyService.StopLowLevelHook();
        _hotkeyService.StartLowLevelHook((vkCode) =>
        {
            var binding = profile?.Bindings.FirstOrDefault(b => b.VirtualKey == (uint)vkCode);
            if (binding != null)
                HandleHotkeyAction(binding.ActionName);
        });

        Log.Information("Hotkeys reloaded from profile");
    }

    private void InitializeHotkeys()
    {
        _hotkeyService.Initialize(_hwnd);

        var profile = _hotkeyRepository.GetDefaultProfile();
        if (profile?.Bindings.Count > 0)
        {
            foreach (var b in profile.Bindings)
            {
                if (b.VirtualKey != 0)
                    _hotkeyService.RegisterHotkey(b.ActionName, b.Modifiers, b.VirtualKey, () => HandleHotkeyAction(b.ActionName));
            }
        }

        _hotkeyService.StartLowLevelHook((vkCode) =>
        {
            var profile = _hotkeyRepository.GetDefaultProfile();
            var binding = profile?.Bindings.FirstOrDefault(b => b.VirtualKey == (uint)vkCode);
            if (binding != null)
                HandleHotkeyAction(binding.ActionName);
        });
    }

    private async void HandleHotkeyAction(string action)
    {
        switch (action)
        {
            case "play_pause":
                await WebViewControl.ExecuteScript("window.__gv_player.togglePlay()");
                break;
            case "fast_forward":
                await WebViewControl.ExecuteScript("window.__gv_player.fastForward(10)");
                break;
            case "fast_backward":
                await WebViewControl.ExecuteScript("window.__gv_player.fastBackward(10)");
                break;
            case "volume_up":
                await WebViewControl.ExecuteScript("window.__gv_player.volumeUp()");
                break;
            case "volume_down":
                await WebViewControl.ExecuteScript("window.__gv_player.volumeDown()");
                break;
            case "toggle_visibility":
                _isVisible = !_isVisible;
                if (_isVisible) AppWindow.Show(); else AppWindow.Hide();
                break;
            case "bookmark_page":
                var url = _tabManager.GetBookmarkUrl();
                var title = _tabManager.GetBookmarkTitle();
                if (!string.IsNullOrEmpty(url) && !string.IsNullOrEmpty(title))
                {
                    int? gameId = ResolveForegroundGameId();
                    _bookmarkService.QuickAdd(title, url, gameId: gameId);
                }
                break;
            case "toggle_subtitle":
                _isSubtitleEnabled = !_isSubtitleEnabled;
                ToggleSubtitleOverlay();
                break;
            case "toggle_minimap":
                _isMiniMapEnabled = !_isMiniMapEnabled;
                ToggleMiniMapOverlay();
                break;
        }
    }

    private void InitializeSubtitle()
    {
        _subtitleService.DirectionWordDetected += (word) =>
            _miniMapOverlay?.ShowDirection(word);
    }

    private void ToggleSubtitleOverlay()
    {
        if (_isSubtitleEnabled)
        {
            _subtitleOverlay = new SubtitleOverlay(_subtitleService);
            _subtitleOverlay.AppWindow.Show();
            _subtitleService.StartSync();
            StartSubtitleTimeSync();
        }
        else
        {
            StopSubtitleTimeSync();
            _subtitleService.StopSync();
            _subtitleOverlay?.Close();
            _subtitleOverlay = null;
        }
    }

    private void StartSubtitleTimeSync()
    {
        _subtitleTimeSyncTimer?.Dispose();
        _subtitleTimeSyncTimer = new System.Timers.Timer(500) { AutoReset = true };
        _subtitleTimeSyncTimer.Elapsed += (s, e) =>
        {
            _ = DispatcherQueue.TryEnqueue(async () =>
            {
                try
                {
                    var json = await WebViewControl.ExecuteScript("window.__gv_player.getTime()");
                    if (!string.IsNullOrEmpty(json))
                    {
                        var doc = System.Text.Json.JsonDocument.Parse(json);
                        var root = doc.RootElement;
                        if (root.TryGetProperty("current", out var current))
                            _subtitleService.UpdateTime(current.GetDouble());
                    }
                }
                catch { }
            });
        };
        _subtitleTimeSyncTimer.Start();
    }

    private void StopSubtitleTimeSync()
    {
        _subtitleTimeSyncTimer?.Dispose();
        _subtitleTimeSyncTimer = null;
    }

    private void ToggleMiniMapOverlay()
    {
        if (_isMiniMapEnabled)
        {
            _miniMapOverlay = new MiniMapOverlay(_directionService);
            _miniMapOverlay.AppWindow.Show();
        }
        else
        {
            _miniMapOverlay?.Close();
            _miniMapOverlay = null;
        }
    }

    private void RestoreWindowState()
    {
        var state = _windowStateRepo.Get("MainWindow");
        if (state == null || AppWindow == null)
            return;

        try
        {
            AppWindow.MoveAndResize(new Windows.Graphics.RectInt32
            {
                X = (int)state.X, Y = (int)state.Y,
                Width = (int)state.Width, Height = (int)state.Height
            });
            _windowManager.SetOpacity(_hwnd, state.Opacity);
            OpacitySlider.Value = state.Opacity;
        }
        catch (Exception ex) { Log.Warning(ex, "Restore window state failed"); }
    }

    private void SaveWindowState()
    {
        var pos = AppWindow.Position;
        var size = AppWindow.Size;
        _windowStateRepo.Save(new WindowState
        {
            WindowName = "MainWindow",
            X = pos.X, Y = pos.Y,
            Width = size.Width, Height = size.Height,
            Opacity = OpacitySlider.Value,
            IsAlwaysOnTop = true
        });
    }

    private void MainWindow_Closed(object sender, WindowEventArgs args)
    {
        SaveWindowState();
        StopSubtitleTimeSync();
        _subtitleOverlay?.Close();
        _miniMapOverlay?.Close();
        _toolbarWindow?.Close();
        _hotkeyService.Dispose();
        _gameDetector.Dispose();
        _subtitleService.Dispose();
        _tabManager.Cleanup();
        Log.Information("Application shutting down");
    }
}
